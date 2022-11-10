﻿using System;
using System.Collections.Generic;
using GoogleTranslatorProvider.GoogleAPI;
using GoogleTranslatorProvider.Helpers;
using GoogleTranslatorProvider.Interfaces;
using GoogleTranslatorProvider.Models;
using GoogleTranslatorProvider.Service;
using Sdl.Core.Globalization;
using Sdl.LanguagePlatform.Core;
using Sdl.LanguagePlatform.TranslationMemory;
using Sdl.LanguagePlatform.TranslationMemoryApi;

namespace GoogleTranslatorProvider.Studio
{
	public class ProviderLanguageDirection : ITranslationProviderLanguageDirection
	{
		private readonly LanguagePair _languageDirection;
		private readonly ITranslationOptions _options;
		private readonly Provider _provider;
		private readonly HtmlUtil _htmlUtil;
		private V2Connector _googleV2Api;
		private V3Connector _googleV3Api;
		private TranslationUnit _inputTu;
		private GTPSegmentEditor _postLookupSegmentEditor;
		private GTPSegmentEditor _preLookupSegmentEditor;

		/// <summary>
		/// Instantiates the variables and fills the list file content into
		/// a Dictionary collection object.
		/// </summary>
		public ProviderLanguageDirection(Provider provider, LanguagePair languages, HtmlUtil htmlUtil)
		{
			_provider = provider;
			_languageDirection = languages;
			_options = _provider.Options;
			_htmlUtil = htmlUtil;
		}

		public bool CanReverseLanguageDirection { get; } = false;

		public System.Globalization.CultureInfo SourceLanguage => _languageDirection.SourceCulture;

		public System.Globalization.CultureInfo TargetLanguage => _languageDirection.TargetCulture;

		public ITranslationProvider TranslationProvider => _provider;

		public SearchResults[] SearchSegments(SearchSettings settings, Segment[] segments)
		{
			var results = new SearchResults[segments.Length];
			for (var p = 0; p < segments.Length; ++p)
			{
				results[p] = SearchSegment(settings, segments[p]);
			}
			return results;
		}

		public SearchResults[] SearchSegmentsMasked(SearchSettings settings, Segment[] segments, bool[] mask)
		{
			if (segments == null)
			{
				throw new ArgumentNullException("segments in SearchSegmentsMasked");
			}
			if (mask == null || mask.Length != segments.Length)
			{
				throw new ArgumentException("mask in SearchSegmentsMasked");
			}

			var results = new SearchResults[segments.Length];
			for (var p = 0; p < segments.Length; ++p)
			{
				if (mask[p])
				{
					results[p] = SearchSegment(settings, segments[p]);
				}
				else
				{
					results[p] = null;
				}
			}
			return results;
		}

		public SearchResults SearchText(SearchSettings settings, string segment)
		{
			var currentSegment = new Segment(_languageDirection.SourceCulture);
			currentSegment.Add(segment);
			return SearchSegment(settings, currentSegment);
		}

		public SearchResults SearchTranslationUnit(SearchSettings settings, TranslationUnit translationUnit)
		{
			//need to use the tu confirmation level in searchsegment method
			_inputTu = translationUnit;
			return SearchSegment(settings, translationUnit.SourceSegment);
		}

		public SearchResults[] SearchTranslationUnits(SearchSettings settings, TranslationUnit[] translationUnits)
		{
			var results = new SearchResults[translationUnits.Length];
			for (var p = 0; p < translationUnits.Length; ++p)
			{
				//need to use the tu confirmation level in searchsegment method
				if (translationUnits[p] == null) continue;
				_inputTu = translationUnits[p];
				results[p] = SearchSegment(settings, translationUnits[p].SourceSegment); //changed this to send whole tu
			}
			return results;
		}

		public SearchResults[] SearchTranslationUnitsMasked(SearchSettings settings, TranslationUnit[] translationUnits, bool[] mask)
		{
			var noOfResults = mask.Length;
			var results = new List<SearchResults>(noOfResults);
			var i = 0;
			foreach (var tu in translationUnits)
			{
				if (mask[i])
				{
					var result = SearchTranslationUnit(settings, tu);
					results.Add(result);
				}
				else
				{
					results.Add(null);
				}
				i++;
			}

			return results.ToArray();
		}

		/// <summary>
		/// Creates the translation unit as it is later shown in the Translation Results
		/// window of SDL Trados Studio. This member also determines the match score
		/// (in our implementation always 100%, as only exact matches are supported)
		/// as well as the confirmation level, i.e. Translated.
		/// </summary>
		private SearchResult CreateSearchResult(Segment searchSegment, Segment translation)
		{
			var tu = new TranslationUnit { SourceSegment = searchSegment.Duplicate(), TargetSegment = translation };
			//this makes the original source segment, with tags, appear in the search window
			tu.ResourceId = new PersistentObjectToken(tu.GetHashCode(), Guid.Empty);

			var score = 0; //score to 0...change if needed to support scoring
			tu.Origin = TranslationUnitOrigin.Nmt;
			var searchResult = new SearchResult(tu) { ScoringResult = new ScoringResult { BaseScore = score } };
			tu.ConfirmationLevel = ConfirmationLevel.Draft;
			searchResult.TranslationProposal = new TranslationUnit(tu);

			return searchResult;
		}

		/// <summary>
		/// Used to do batch find-replace on a segment with tags.
		/// </summary>
		private Segment GetEditedSegment(GTPSegmentEditor editor, Segment inSegment)
		{
			var newSeg = new Segment(inSegment.Culture);

			foreach (var element in inSegment.Elements)
			{
				var elType = element.GetType();

				if (elType.ToString() != "Sdl.LanguagePlatform.Core.Tag") //if other than tag, make string and edit it
				{
					var temp = editor.EditText(element.ToString());
					newSeg.Add(temp); //add edited text to segment
				}
				else
				{
					newSeg.Add(element); //if tag just add the tag
				}
			}
			return newSeg;
		}

		/// <summary>
		/// Used to do batch find-replace on a string of plain text.
		/// </summary>
		private string GetEditedString(GTPSegmentEditor editor, string sourcetext)
		{
			var result = editor.EditText(sourcetext);
			return result;
		}

		private string LookupGt(string sourcetext, ITranslationOptions options, string format)
		{
			if (options.SelectedGoogleVersion == ApiVersion.V2)
			{
				//instantiate GtApiConnecter if necessary
				if (_googleV2Api == null)
				{
					// need to get and insert key
					_googleV2Api = new V2Connector(options.ApiKey, _htmlUtil); //needs key
				}
				else
				{
					//reset key in case it has been changed in dialog since GtApiConnecter was instantiated
					_googleV2Api.ApiKey = options.ApiKey;
				}

				var translatedText = _googleV2Api.Translate(_languageDirection, sourcetext, format);

				return translatedText;
			}
			_googleV3Api = new V3Connector(options);

			var v3TranslatedText =
				_googleV3Api.TranslateText(_languageDirection.SourceCulture, _languageDirection.TargetCulture, sourcetext, format);

			return v3TranslatedText;
		}

		/// <summary>
		/// Performs the actual search by looping through the
		/// delimited segment pairs contained in the text file.
		/// Depening on the search mode, a segment lookup (with exact machting) or a source / target
		/// concordance search is done.
		/// </summary>
		public SearchResults SearchSegment(SearchSettings settings, Segment segment)
		{
			var translation = new Segment(_languageDirection.TargetCulture);//this will be the target segment

			var results = new SearchResults
			{
				SourceSegment = segment.Duplicate()
			};

			if (!_options.ResendDrafts && _inputTu.ConfirmationLevel != ConfirmationLevel.Unspecified) //i.e. if it's status is other than untranslated
			{ //don't do the lookup, b/c we don't need to pay google to translate text already translated if we edit a segment
				translation.Add(PluginResources.TranslationLookupDraftNotResentMessage);
				//later get these strings from resource file
				results.Add(CreateSearchResult(segment, translation));
				return results;
			}

			// Look up the currently selected segment in the collection (normal segment lookup).

			var translatedText = "";
			//a new seg avoids modifying the current segment object
			var newseg = segment.Duplicate();

			//do preedit if checked
			var sendTextOnly = _options.SendPlainTextOnly || !newseg.HasTags;
			if (!sendTextOnly)
			{
				//do preedit with tagged segment
				if (_options.UsePreEdit)
				{
					if (_preLookupSegmentEditor == null) _preLookupSegmentEditor = new GTPSegmentEditor(_options.PreLookupFilename);
					newseg = GetEditedSegment(_preLookupSegmentEditor, newseg);
				}
				//return our tagged target segment
				var tagplacer = new TagPlacer(newseg, _htmlUtil);
				////tagplacer is constructed and gives us back a properly marked up source string for google
				if (_options.SelectedProvider == ProviderType.GoogleTranslate)
				{
					translatedText = LookupGt(tagplacer.PreparedSourceText, _options, "html");
				}
				//now we send the output back to tagplacer for our properly tagged segment
				translation = tagplacer.GetTaggedSegment(translatedText).Duplicate();

				//now do post-edit if that option is checked
				if (_options.UsePostEdit)
				{
					if (_postLookupSegmentEditor == null) _postLookupSegmentEditor = new GTPSegmentEditor(_options.PostLookupFilename);
					translation = GetEditedSegment(_postLookupSegmentEditor, translation);
				}
			}
			else //only send plain text
			{
				var sourcetext = newseg.ToPlain();
				//do preedit with string
				if (_options.UsePreEdit)
				{
					if (_preLookupSegmentEditor == null) _preLookupSegmentEditor = new GTPSegmentEditor(_options.PreLookupFilename);
					sourcetext = GetEditedString(_preLookupSegmentEditor, sourcetext);
					//change our source segment so it gets sent back with modified text to show in translation results window that it was changed before sending
					newseg.Clear();
					newseg.Add(sourcetext);
				}

				switch (_options.SelectedProvider)
				{
					//now do lookup
					case ProviderType.GoogleTranslate:
						translatedText = LookupGt(sourcetext, _options, "text");
						break;
					default:
						break;
				}
				//now do post-edit if that option is checked
				if (_options.UsePostEdit)
				{
					if (_postLookupSegmentEditor == null) _postLookupSegmentEditor = new GTPSegmentEditor(_options.PostLookupFilename);
					translatedText = GetEditedString(_postLookupSegmentEditor, translatedText);
				}
				translation.Add(translatedText);
			}

			results.Add(CreateSearchResult(newseg, translation));

			return results;

		}

		/// <summary>
		/// Not required for this implementation.
		/// </summary>
		public ImportResult[] AddOrUpdateTranslationUnits(TranslationUnit[] translationUnits, int[] previousTranslationHashes, ImportSettings settings)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Not required for this implementation.
		/// </summary>
		public ImportResult[] AddOrUpdateTranslationUnitsMasked(TranslationUnit[] translationUnits, int[] previousTranslationHashes, ImportSettings settings, bool[] mask)
		{
			ImportResult[] result = { AddTranslationUnit(translationUnits[translationUnits.GetLength(0) - 1], settings) };
			return result;
		}

		/// <summary>
		/// Not required for this implementation.
		/// </summary>
		/// <param name="translationUnit"></param>
		/// <param name="settings"></param>
		/// <returns></returns>
		public ImportResult AddTranslationUnit(TranslationUnit translationUnit, ImportSettings settings)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Not required for this implementation.
		/// </summary>
		public ImportResult[] AddTranslationUnits(TranslationUnit[] translationUnits, ImportSettings settings)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Not required for this implementation.
		/// </summary>
		public ImportResult[] AddTranslationUnitsMasked(TranslationUnit[] translationUnits, ImportSettings settings, bool[] mask)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Not required for this implementation.
		/// </summary>
		public ImportResult UpdateTranslationUnit(TranslationUnit translationUnit)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Not required for this implementation.
		/// </summary>
		public ImportResult[] UpdateTranslationUnits(TranslationUnit[] translationUnits)
		{
			throw new NotImplementedException();
		}

	}

}
