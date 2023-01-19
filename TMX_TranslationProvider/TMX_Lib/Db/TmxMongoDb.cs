﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using NLog;
using Sdl.Core.Globalization.NumberMetadata;
using TMX_Lib.TmxFormat;
using TMX_Lib.Utils;

namespace TMX_Lib.Db
{
    // example:
    // TmxMongoDb db = new TmxMongoDb("localhost:27017", "mydb");
    public class TmxMongoDb
    {
	    private static readonly Logger log = NLog.LogManager.GetCurrentClassLogger();


		public const int DEFAULT_ENTRIES_PER_TEXT_TABLE = 50000;

        // FIXME test with Atlas credentials as well
		private string _url;
        private string _databaseName;
        private bool _connected;
        private bool _initialized;

        private MongoClient _client;
        private IMongoDatabase _database;

        private IMongoCollection<TmxMeta> _metas;
        private IMongoCollection<TmxLanguage> _languages;
        private IMongoCollection<TmxTranslationUnit> _translationUnits;
        private TmxTextsArray _texts;

        private TmxMeta _translationUnitMeta;
        private ulong _nextTranslationUnitID = 0;

        private string _importError = "";

        private Task _importTask;
        private CancellationTokenSource _importTokenSource = new CancellationTokenSource();
		// if null - no import running
		// true or false - import is either running or complete
        private bool? _importComplete = null;
        private TmxParser _parser;

        private CultureDictionary _cultures = new CultureDictionary();

		private int _entriesPerTextTable;

		public TmxMongoDb(string url, string databaseName, int entriesPerTextTable = DEFAULT_ENTRIES_PER_TEXT_TABLE)
		{
			_url = url;
			var notAllowedChars = " \t\r\n:.()_";
			_databaseName = new string(databaseName.Where(ch => !notAllowedChars.Contains(ch)).ToArray());
			_entriesPerTextTable = entriesPerTextTable;
			Connect();
		}

		public TmxMongoDb(string databaseName, int entriesPerTextTable = DEFAULT_ENTRIES_PER_TEXT_TABLE) : this("localhost:27017", databaseName, entriesPerTextTable)
		{
		}

		private bool IsLocalConnection() => _url.StartsWith("mongodb://");

		public string ImportError() => _importError;

		public ulong MaxTranslationId()
		{
			lock (this)
				return _nextTranslationUnitID ;
		}

        private void Connect()
        {
            try
            {
	            if (!_url.StartsWith("mongo"))
		            _url = $"mongodb://{_url}";

				var url = new MongoUrl(_url);
                _client = new MongoClient(url);
                _database = _client.GetDatabase(_databaseName);
                _connected = true;
				// if local -> fast connection
                if (IsLocalConnection())
                {
	                InitializeImpl();
	                lock (this)
						_initialized = true;
                }
			}
            catch (TimeoutException e)
            {
                throw new TmxException($"Timeout connecting to {_databaseName}");
            }
            catch (Exception e)
            {
                throw new TmxException($"Can't connect to {_databaseName} database." , e);
            }
        }

        public static async Task<IReadOnlyList<string>> GetLocalDatabaseNamesAsync()
        {
	        List<string> names = new List<string>();
	        await Task.Run(async () =>
	        {
		        try
		        {
			        var url = new MongoUrl("mongodb://localhost:27017");
			        var client = new MongoClient(url);
			        var list = await client.ListDatabaseNamesAsync();
			        var defaultNames = new[] { "admin", "config", "local"};
			        await list.ForEachAsync(n =>
			        {
						if (!defaultNames.Contains(n))
							names.Add(n);
			        });

		        }
		        catch (Exception e)
		        {
					log.Error($"can't get local db names : {e}");
		        }
	        });
	        return names;
        }

        private Task _initTask;
        public async Task InitAsync()
        {
	        try
	        {
				lock(this)
					if (_initialized)
						return;

				if (_initTask == null)
					_initTask = Task.Run(async () => await InitializeImpl());
		        await _initTask;
	        }
			finally
	        {
		        lock (this)
			        _initialized = true;
	        }
        }

		// this connects to the server, be it local or remote - which can block the current thread
        private async Task InitializeImpl()
        {
            if (!_connected)
                return;
            try
            {
	            var names = _database.ListCollectionNames().ToList();
	            if (!names.Any())
	            {
		            _database.CreateCollection("languages");
		            _database.CreateCollection("meta");
		            _database.CreateCollection("translation_units");
	            }

	            _metas = _database.GetCollection<TmxMeta>("meta");
	            _languages = _database.GetCollection<TmxLanguage>("languages");
	            _translationUnits = _database.GetCollection<TmxTranslationUnit>("translation_units");
	            _texts = new TmxTextsArray(_client, _database, _entriesPerTextTable);

	            var filter = Builders<TmxMeta>.Filter.Where(m => m.Type == "TranslationUnitNextID");
	            var cursor = await _metas.FindAsync(filter);
	            await cursor.ForEachAsync(m => _translationUnitMeta = m);
	            if (_translationUnitMeta != null)
		            _nextTranslationUnitID = ulong.Parse(_translationUnitMeta.Value);
	            else
		            _nextTranslationUnitID = 1;
            }
            catch (TimeoutException te)
            {
	            throw new TmxException($"Timeout connecting to database {_url}", te);
            }
			catch (Exception e)
            {
                throw new TmxException($"Can't connect to database {_url}", e);
            }
        }

        public async Task ClearAsync()
        {
            try
            {
                if (HasAnyData())
                {
                    // remove everything, we'll restart the import from scratch
                    await _metas.DeleteManyAsync(Builders<TmxMeta>.Filter.Empty);
                    await _languages.DeleteManyAsync(Builders<TmxLanguage>.Filter.Empty);
                    await _translationUnits.DeleteManyAsync(Builders<TmxTranslationUnit>.Filter.Empty);
                    await _texts.ClearAsync();
                }
            }
            catch (Exception e)
            {
                throw new TmxException("Can't initialize db", e);
            }
        }

        public bool HasAnyData()
        {
            try
            {
                return _metas.EstimatedDocumentCount() > 0;
            }
            catch (Exception e)
            {
                throw new TmxException("Error accessing db - HasAnyData", e);
            }
        }

        public async Task<string> ImportedFileNameAsync()
        {
	        try
	        {
		        var result = await _metas.FindAsync(Builders<TmxMeta>.Filter.Where(m => m.Type == "FileName"));
		        var metas = new List<string>();
		        await result.ForEachAsync(l => metas.Add(l.Value));
		        return metas.Count > 0 ? metas[0] : "";
	        }
			catch (Exception e)
	        {
		        throw new TmxException("Can't get imported file name", e);
	        }
		}

        public async Task<IReadOnlyList<string>> GetAllLanguagesAsync()
        {
	        try
	        {
		        var result = await _languages.FindAsync(Builders<TmxLanguage>.Filter.Empty);
		        var languages = new List<string>();
		        await result.ForEachAsync(l => languages.Add(l.Language));
		        return languages;
	        }
	        catch (Exception e)
	        {
		        throw new TmxException("Can't get all languages", e);
	        }
        }

        private async Task CreateIndexesAsync()
        {
            try
            {
	            var list = await _translationUnits.Indexes.ListAsync();
	            int count = 0;
	            await list.ForEachAsync(c => ++count);
	            if (count > 1)
		            return;

				var indexTU = Builders<TmxTranslationUnit>.IndexKeys.Ascending(i => i.TranslationUnitID);
	            await _translationUnits.Indexes.CreateOneAsync(new CreateIndexModel<TmxTranslationUnit>(indexTU));

				await _texts.CreateIndexesAsync();
            }
			catch (Exception e)
            {
                throw new TmxException("Can't create indexes", e);
            }
        }
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// Start of SEARCH

		private string XmlUnformatText(string formattedText)
		{
			XmlDocument doc = new XmlDocument();
			formattedText = formattedText.Replace("<", "&lt;").Replace(">", "&gt;");
			doc.LoadXml($"<tmx>{formattedText}</tmx>");
			var unformatted = doc.SelectSingleNode("tmx").InnerText;
			return unformatted;
		}

		// if not found, returns null
		private async Task<TmxSegment> TryGetSegment(TmxText text, string targetLanguage)
		{
			var targetText = await _texts.TryFindTranslation(text.TranslationUnitID, targetLanguage);

			var tuFilter = Builders<TmxTranslationUnit>.Filter.Where(f => f.TranslationUnitID == text.TranslationUnitID);
			var tuCursor = await _translationUnits.FindAsync(tuFilter, new FindOptions<TmxTranslationUnit> { Limit = 1 });
			var tus = new List<TmxTranslationUnit>();
			await tuCursor.ForEachAsync(t => tus.Add(t));
			if (tus.Count < 1)
				throw new TmxException($"Can't find Translation Unit with ID {text.TranslationUnitID}");

			var segment = new TmxSegment
			{
				DbTU = tus[0], 
				DbSourceText = text, 
				DbTargetText = targetText,
				SourceText = XmlUnformatText(text.FormattedText),
				TargetText = XmlUnformatText(targetText.FormattedText),
			};

			return segment;
		}

		// this just performs the search and returns the results -- does NOT perform any other analyses, like, compare score and such
		public async Task<IReadOnlyList<TmxSegment>> ExactSearch(string text, string sourceLanguage, string targetLanguage)
		{
			text = Util.TextToDbText(text, _cultures.Culture(sourceLanguage));
			sourceLanguage = Util.NormalizeLanguage(sourceLanguage);
			targetLanguage = Util.NormalizeLanguage(targetLanguage);

			var texts = await _texts.ExactSearch(text, sourceLanguage);
			var segments = new List<TmxSegment>();
			foreach (var t in texts)
			{
				var segment = await TryGetSegment(t, targetLanguage);
				if (segment != null)
					segments.Add(segment);
			}
			return segments;
		}
		// this just performs the search and returns the results -- does NOT perform any other analyses, like, compare score and such
		public async Task<IReadOnlyList<TmxSegment>> FuzzySearch(string text, string sourceLanguage, string targetLanguage)
		{
			text = Util.TextToDbText(text, _cultures.Culture(sourceLanguage));
			sourceLanguage = Util.NormalizeLanguage(sourceLanguage);
			targetLanguage = Util.NormalizeLanguage(targetLanguage);

			var texts = await _texts.FuzzySearch(text, sourceLanguage);
			var segments = new List<TmxSegment>();
			foreach (var t in texts)
			{
				var segment = await TryGetSegment(t, targetLanguage);
				if (segment != null)
					segments.Add(segment);
			}
			return segments;
		}

		public async Task<IReadOnlyList<TmxSegment>> ConcordanceSearch(string text, string sourceLanguage, string targetLanguage)
		{
			sourceLanguage = Util.NormalizeLanguage(sourceLanguage);
			targetLanguage = Util.NormalizeLanguage(targetLanguage);
			var texts = await _texts.ConcordanceSearch(text, sourceLanguage);
			var segments = new List<TmxSegment>();
			foreach (var t in texts)
			{
				var segment = await TryGetSegment(t, targetLanguage);
				if (segment != null)
					segments.Add(segment);
			}
			return segments;
		}
		// End of SEARCH
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		


		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// Start of FIND

		public async Task<TmxTranslationUnit> TryFindTranslationUnitAsync(ulong id)
		{
			var filter = Builders<TmxTranslationUnit>.Filter.Where(tu => tu.TranslationUnitID == id);
			var cursor = await _translationUnits.FindAsync(filter);
			TmxTranslationUnit result = null;
			await cursor.ForEachAsync(t =>
			{
				result = t;
			});
			return result;
		}

		public async Task<TmxTranslationUnit> FindTranslationUnitAsync(ulong id)
		{
			var result = await TryFindTranslationUnitAsync(id);
			if (result == null)
				throw new TmxException($"Translation Unit {id} not found");
			return result;
		}

		public async Task<IReadOnlyList<TmxText>> FindTextsAsync(ulong id)
		{
			return await _texts.FindTextsAsync(id);
		}

		public async Task<TmxMeta> TryFindMetaAsync(string type)
		{
			var filter = Builders<TmxMeta>.Filter.Where(m => m.Type == type);
			var cursor = await _metas.FindAsync(filter);
			TmxMeta meta = null;
			await cursor.ForEachAsync(m =>
			{
				meta = m;
			});
			return meta;
		}

		public async Task<TmxMeta> FindMetaAsync(string type)
		{
			var result = await TryFindMetaAsync(type);
			if (result == null)
				throw new TmxException($"Meta {type} not found");
			return result;
		}

		// End of FIND
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// Start of ADDs

		public async Task AddMetasAsync(IEnumerable<TmxMeta> metas, CancellationToken token = default(CancellationToken))
        {
            try
            {
                await _metas.InsertManyAsync(metas, null, token);
            }
            catch (Exception e)
            {
                throw new TmxException($"Can't add Metas", e);
            }
        }
        public async Task AddLanguagesAsync(IEnumerable<TmxLanguage> languages, CancellationToken token = default(CancellationToken))
        {
            try
            {
                await _languages.InsertManyAsync(languages, null, token);
            }
            catch (Exception e)
            {
                throw new TmxException($"Can't add Languages", e);
            }
        }
        public async Task AddTextsAsync(IEnumerable<TmxText> texts, CancellationToken token = default(CancellationToken))
        {
			await _texts.AddTextsAsync(texts);
        }
        public async Task AddTranslationUnitsAsync(IEnumerable<TmxTranslationUnit> translationUnits, CancellationToken token = default(CancellationToken))
        {
            try
            {
                await _translationUnits.InsertManyAsync(translationUnits, null, token);
            }
            catch (Exception e)
            {
                throw new TmxException($"Can't add TranslationUnits", e);
            }
        }

		// at end of function, the translation unit ID is set
        public async Task AddSingleTranslationUnitAsync(TmxTranslationUnit translationUnit, CancellationToken token = default(CancellationToken))
        {
	        try
	        {
				Debug.Assert(translationUnit.TranslationUnitID == 0);
				lock (this)
				{
					translationUnit.TranslationUnitID = _nextTranslationUnitID++;
					if (_translationUnitMeta == null)
						_translationUnitMeta = new TmxMeta { Type = "TranslationUnitNextID" };
					_translationUnitMeta.Value = _nextTranslationUnitID.ToString();
				}
				await AddTranslationUnitsAsync(new[] { translationUnit }, token);
				await UpdateMetaAsync(_translationUnitMeta, token);
	        }
	        catch (Exception e)
	        {
		        throw new TmxException($"Can't add TranslationUnits", e);
	        }
        }

		// End of ADDs
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////



		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// Start of UPDATE

		public async Task UpdateTextAsync(TmxText text, CancellationToken token = default(CancellationToken))
		{
			try
			{
				var filter = Builders<TmxText>.Filter.Where(t => t.TranslationUnitID == text.TranslationUnitID && t.NormalizedLanguage == text.NormalizedLanguage);
				// FIXME reimplement
				//await _texts.FindOneAndReplaceAsync(filter, text, cancellationToken: token);
			}
			catch (Exception e)
			{
				throw new TmxException($"Can't update text", e);
			}
		}

		public async Task UpdateTranslationUnitAsync(TmxTranslationUnit translationUnit, CancellationToken token = default(CancellationToken))
		{
			try
			{
				var filter = Builders<TmxTranslationUnit>.Filter.Where(t => t.TranslationUnitID == translationUnit.TranslationUnitID);
				await _translationUnits.FindOneAndReplaceAsync(filter, translationUnit, cancellationToken: token);
			}
			catch (Exception e)
			{
				throw new TmxException($"Can't update TranslationUnit", e);
			}
		}

		public async Task UpdateMetaAsync(TmxMeta meta, CancellationToken token = default(CancellationToken))
		{
			try
			{
				var filter = Builders<TmxMeta>.Filter.Where(t => t.Type == meta.Type);
				await _metas.FindOneAndReplaceAsync(filter, meta, cancellationToken: token);
			}
			catch (Exception e)
			{
				throw new TmxException($"Can't update Meta", e);
			}
		}

		// End of UPDATE
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////



		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// Start of IMPORT

		private static (IReadOnlyList<Db.TmxText> texts, Db.TmxTranslationUnit tu) TmxTranslationUnitToMongo(TmxFormat.TmxTranslationUnit tu, ulong id)
		{
			var dbTU = new Db.TmxTranslationUnit
			{
				TranslationUnitID = id,
				CreationAuthor = tu.CreationAuthor,
				CreationDate = tu.CreationTime,
				ChangeAuthor = tu.ChangeAuthor,
				ChangeDate = tu.ChangeTime,
				XmlProperties = tu.XmlProperties,
				TuAttributes = tu.TuAttributes,
				NormalizedLanguages = tu.Texts.Select(t => Util.NormalizeLanguage(t.Language)).ToList(),
			};

			var texts = tu.Texts.Select(t => new Db.TmxText
			{
				TranslationUnitID = id,
				NormalizedLanguage = Util.NormalizeLanguage(t.Language) ,
				LocaseText = t.LocaseText,
				FormattedText = t.FormattedText,
			}).ToList();

			return (texts, dbTU);
		}

		// when i have a new database connection - this is the first question - have we already imported everything?
		public async Task<bool> HasImportBeenDoneBeforeAsync()
        {
	        try
	        {
		        var list = await _translationUnits.Indexes.ListAsync();
		        int count = 0;
		        await list.ForEachAsync(c => ++count);

		        // the idea - create indexes is the last step
		        // if translation units table has an extra index, we've done it
		        return count > 1;
	        }
	        catch (Exception e)
	        {
		        throw new TmxException("Can't check if import is complete", e);
	        }
        }

        public bool IsImportInProgress()
        {
	        lock (this)
		        return _importComplete != null;
        }

        public bool IsImportComplete()
        {
	        lock (this)
		        return _importComplete == true;
        }

        public double ImportProgress()
        {
	        lock (this)
	        {
		        if (_importComplete == true)
			        return 1;
		        var progress = _parser?.Progress() ?? 0d;
				// the idea - once the parsing is complete, I'm creating the indexes. 
				// I assume that will take some time as well
		        return Math.Min(progress * 0.95, 0.95);
	        }
        }

        public void CancelImport()
        {
	        lock (this)
	        {
		        if (_importTask != null)
			        return;
	        }

	        _importTokenSource.Cancel();
        }

        public async Task ImportToDbAsync(string fileName, Action<TmxImportReport> updateReport = null, bool quickImport = false)
        {
	        Task import;
	        lock (this)
	        {
		        if (_importComplete != null)
		        {
			        if (_importComplete == true)
				        return;
					else
						throw new TmxException("Import already in progress");
		        }
				if (_importTask == null)
			        _importTask = Task.Run(() => ImportToDbAsyncImpl(_parser = new TmxParser(fileName, quickImport) , updateReport, _importTokenSource.Token));

		        import = _importTask;
		        _importComplete = false;
	        }

	        await import;
        }

		// does NOT throw
		private async Task ImportToDbAsyncImpl(TmxParser parser, Action<TmxImportReport> updateReport , CancellationToken token)
        {
	        Stopwatch watch = null;
	        try
	        {
		        lock (this)
			        _importError = "";

		        var report = TmxImportReport.StartNow();
		        var headerInvalidChars = parser.InvalidCharsNodeCount;
		        report.TUsWithInvalidChars += headerInvalidChars;
		        report.TUsRead += headerInvalidChars;

		        await ClearAsync();
		        watch = Stopwatch.StartNew();

		        ulong id = 1;
		        while (true)
		        {
			        if (token.IsCancellationRequested)
				        break;
			        var oldInvalidCharsNodeCount = parser.InvalidCharsNodeCount;
			        var oldInvalidNodeCount = parser.InvalidNodeCount;
			        var oldSuccessCount = parser.SuccessCount;
			        var TUs = parser.TryReadNextTUs();
			        var invalidCharsCount = (parser.InvalidCharsNodeCount - oldInvalidCharsNodeCount);
			        var ignored = (parser.InvalidNodeCount - oldInvalidNodeCount);
			        var success = parser.SuccessCount - oldSuccessCount;

					report.TUsWithInvalidChars += invalidCharsCount;
			        report.TUsWithSyntaxErrors += ignored;
			        report.TUsRead += success + ignored + invalidCharsCount;
			        report.TUsImportedSuccessfully += success;
			        if (TUs == null)
				        break;

			        var dbTexts = new List<TmxText>();
			        var dbTUs = new List<TmxTranslationUnit>();
			        foreach (var tu in TUs)
			        {
				        var dbTU = TmxTranslationUnitToMongo(tu, id++);
				        dbTexts.AddRange(dbTU.texts);
				        dbTUs.Add(dbTU.tu);
			        }

			        await AddTranslationUnitsAsync(dbTUs, token);
			        await AddTextsAsync(dbTexts, token);
			        updateReport?.Invoke(report);
		        }

				if (!parser.HasError)
		        {
			        // languages are known only after everything has been imported
			        var languages = parser.Languages().Select(l => new TmxLanguage { Language = l }).ToList();
			        await AddLanguagesAsync(languages, token);
			        report.LanguageCount = languages.Count;
			        _nextTranslationUnitID = id;

			        // the idea -> add them at the end, easily know if the import went to the end or not
			        await AddMetasAsync(new[]
			        {
				        new TmxMeta { Type = "Header", Value = parser.Header.Xml, },
				        // Version:
				        // 1 - initial version
						// 2 - added TranslationUnitNextID + TU.NormalizedLanguages array
						// 3 - different tables for each language + each language can have more than one text table
				        // ... - increase this on each breaking change
				        new TmxMeta { Type = "Version", Value = "3", }, 
				        new TmxMeta { Type = "Source Language", Value = parser.Header.SourceLanguage, },
				        new TmxMeta { Type = "Target Language", Value = parser.Header.TargetLanguage, },
				        new TmxMeta { Type = "Domains", Value = string.Join(", ", parser.Header.Domains), },
				        new TmxMeta { Type = "Creation Date", Value = parser.Header.CreationDate?.ToLongDateString() ?? "unknown", },
				        new TmxMeta { Type = "Author", Value = parser.Header.Author, }, 
				        new TmxMeta { Type = "FileName", Value = Path.GetFileName(parser.FileName) },
				        new TmxMeta { Type = "FullFileName", Value = parser.FileName },
				        new TmxMeta { Type = "TranslationUnitNextID", Value = id.ToString() },
			        }, token);
			        updateReport?.Invoke(report);

					// best practice - create indexes after everything has been imported
					// note: still, this may take a LOOONG time
					if (!token.IsCancellationRequested)
				        await CreateIndexesAsync();
		        }
		        else
		        {
			        lock (this)
				        _importError = parser.Error;
			        report.FatalError = parser.Error;
		        }

		        report.EndTime = DateTime.Now;
		        updateReport?.Invoke(report);
	        }
			catch (Exception e)
	        {
				log.Error($"Import to db failed: {e}");
				lock(this)
					_importError = e.Message;
	        }
	        finally
	        {
		        lock (this)
		        {
			        _importComplete = true;
			        _importTask = null;
			        _importTokenSource = new CancellationTokenSource();
		        }
	        }

			log.Debug($"import complete, took {watch?.ElapsedMilliseconds} ms");
			Console.WriteLine($"import complete, took {watch?.ElapsedMilliseconds} ms");
		}

		// End of IMPORT
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	}
}
