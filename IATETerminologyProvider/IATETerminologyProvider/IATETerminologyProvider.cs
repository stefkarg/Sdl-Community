﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IATETerminologyProvider.Helpers;
using IATETerminologyProvider.Model;
using IATETerminologyProvider.Service;
using Sdl.Terminology.TerminologyProvider.Core;
using Sdl.TranslationStudioAutomation.IntegrationApi;

namespace IATETerminologyProvider
{
	public class IATETerminologyProvider : AbstractTerminologyProvider
	{
		#region Private Fields
		private ProviderSettings _providerSettings;
		private IList<ISearchResult> _termsResult = new List<ISearchResult>();
		private IList<EntryModel> _entryModels = new List<EntryModel>();
		#endregion

		#region Constructors
		public IATETerminologyProvider(ProviderSettings providerSettings)
		{
			_providerSettings = providerSettings;
		}
		#endregion

		#region Public Properties
		public const string IATEUriTemplate = "iateglossary://";
		public override IDefinition Definition => new Definition(GetDescriptiveFields(), GetDefinitionLanguages());
		public override string Description => PluginResources.IATETerminologyProviderDescription;
		public override string Name => PluginResources.IATETerminologyProviderName;
		public override Uri Uri => new Uri((IATEUriTemplate + "https://iate.europa.eu/em-api/entries/_search").RemoveUriForbiddenCharacters());
		#endregion

		#region Public Methods
		public override IEntry GetEntry(int id)
		{
			return _entryModels.FirstOrDefault(e => e.Id == id);
		}

		public override IEntry GetEntry(int id, IEnumerable<ILanguage> languages)
		{
			return _entryModels.FirstOrDefault(e => e.Id == id);
		}

		public override IList<ILanguage> GetLanguages()
		{
			return GetDefinitionLanguages().Cast<ILanguage>().ToList();
		}

		public override IList<ISearchResult> Search(string text, ILanguage source, ILanguage destination, int maxResultsCount, SearchMode mode, bool targetRequired)
		{
			var searchService = new TermSearchService(_providerSettings);
			var t = Task.Factory.StartNew(() =>
			{
				_termsResult = searchService.GetTerms(text, source, destination, maxResultsCount);
			});
			t.Wait();

			if (_termsResult.Count > 0)
			{
				CreateEntryModels(text, source, destination);
			}
			return _termsResult;
		}

		public IList<IDescriptiveField> GetDescriptiveFields()
		{
			var result = new List<IDescriptiveField>();

			var approvedField = new DescriptiveField
			{
				Label = "Approved",
				Level = FieldLevel.TermLevel,
				Mandatory = false,
				Multiple = true,
				PickListValues = new List<string> { "Approved", "Not Approved" },
				Type = FieldType.String
			};
			result.Add(approvedField);

			return result;
		}

		public IList<IDefinitionLanguage> GetDefinitionLanguages()
		{
			var result = new List<IDefinitionLanguage>();
			var currentProject = GetProjectController().CurrentProject;
			var projTargetLanguage = currentProject.GetTargetLanguageFiles()[0].Language;
			var projSourceLanguage = currentProject.GetSourceLanguageFiles()[0].Language;

			var sourceLanguage = new DefinitionLanguage
			{
				IsBidirectional = true,
				Locale = projSourceLanguage.CultureInfo,
				Name = projSourceLanguage.DisplayName,				
				TargetOnly = false
			};

			result.Add(sourceLanguage);

			var targetLanguage = new DefinitionLanguage
			{
				IsBidirectional = true,
				Locale = projTargetLanguage.CultureInfo,
				Name = projTargetLanguage.DisplayName,
				TargetOnly = false
			};

			result.Add(targetLanguage);
			return result;
		}

		public ProjectsController GetProjectController()
		{
			return SdlTradosStudio.Application.GetController<ProjectsController>();
		}
		#endregion

		#region Private Methods
		// Create entry models (used to return the text in the Termbase Search panel)
		private void CreateEntryModels(string text, ILanguage sourceLanguage, ILanguage targetLanguage)
		{
			var languages = GetLanguages();
			_entryModels.Clear();
			foreach (SearchResultModel termResult in _termsResult)
			{
				var entryModel = new EntryModel
				{
					SearchText = text,
					Id = termResult.Id,
					Fields = SetEntryFields(termResult.Definition),
					Transactions = new List<IEntryTransaction>(),
					Languages = SetEntryLanguages(languages, text, sourceLanguage, termResult.Id, termResult.Definition)
				};
				_entryModels.Add(entryModel);
			}
		}

		// Set entry languages for the entry models
		private IList<IEntryLanguage> SetEntryLanguages(IList<ILanguage> languages, string text, ILanguage sourceLanguage, int id, string fieldDefinition)
		{
			IList<IEntryLanguage> entryLanguages = new List<IEntryLanguage>();
			foreach (var language in languages)
			{
				var entryLanguage = new EntryLanguageModel
				{
					Fields = SetEntryFields(fieldDefinition),
					Locale = language.Locale,
					Name = language.Name,
					ParentEntry = null,
					Terms = CreateEntryTerms(language, text, sourceLanguage, id),
					IsSource = language.Name.Equals(sourceLanguage.Name) ? true : false
				};
				entryLanguages.Add(entryLanguage);
			}
			return entryLanguages;
		}

		// Create Entry terms for the entry languages
		private IList<IEntryTerm> CreateEntryTerms(ILanguage language, string text, ILanguage sourceLanguage, int id)
		{
			IList<IEntryTerm> entryTerms = new List<IEntryTerm>();

			// if language is Source language, create entryTerm with value from source language text
			// otherwise create entry terms for the target language search results
			if (language.Name.Equals(sourceLanguage.Name))
			{
				var entryTerm = new EntryTerm
				{
					Value = text
				};
				entryTerms.Add(entryTerm);
			}
			else
			{
				// add IEntryTerm only for the current ISearchResult term(otherwise it will duplicate all the term for each ISearchResult term)
				var terms = _termsResult.Where(t => t.Id == id).ToList();
				foreach (var term in terms)
				{
					var entryTerm = new EntryTerm
					{
						Value = term.Text
					};
					entryTerms.Add(entryTerm);
				}
			}
			return entryTerms;
		}

		// Set field definition
		private IList<IEntryField> SetEntryFields(string fieldDefinition)
		{
			IList<IEntryField> entryFields = new List<IEntryField>();
			if (!string.IsNullOrEmpty(fieldDefinition))
			{
				var entryField = new EntryField
				{
					Name = "Definition",
					Value = fieldDefinition
				};
				entryFields.Add(entryField);
			}
			return entryFields;
		}
		#endregion
	}
}