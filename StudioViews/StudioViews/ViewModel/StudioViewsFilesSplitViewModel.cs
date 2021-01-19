﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Sdl.Community.StudioViews.Commands;
using Sdl.Community.StudioViews.Controls.Folder;
using Sdl.Community.StudioViews.Model;
using Sdl.Community.StudioViews.Services;
using Sdl.ProjectAutomation.Core;
using MessageBox = System.Windows.MessageBox;
using Task = System.Threading.Tasks.Task;

namespace Sdl.Community.StudioViews.ViewModel
{
	public class StudioViewsFilesSplitViewModel : BaseModel
	{
		private readonly Window _window;
		private readonly SdlxliffMerger _sdlxliffMerger;
		private readonly SdlxliffExporter _sdlxliffExporter;
		private readonly SdlxliffReader _sdlxliffReader;
		private readonly List<ProjectFile> _selectedFiles;
		private readonly CommonService _commonService;

		private int _maxNumberOfWords;
		private int _numberOfEqualParts;
		private string _segmentIdsString;
		private bool _splitByWordCount;
		private bool _splitByEqualParts;
		private bool _splitBySegmentIds;
		private string _exportPath;
		private string _fileName;
		private bool _exportPathIsValid;
		private bool _fileNameIsValid;
		private bool _progressIsVisible;

		private ICommand _okCommand;
		private ICommand _resetCommand;
		private ICommand _exportPathBrowseCommand;
		private ICommand _openFolderInExplorerCommand;

		public StudioViewsFilesSplitViewModel(Window window, List<ProjectFile> selectedFiles, CommonService commonService,
			SdlxliffMerger sdlxliffMerger, SdlxliffExporter sdlxliffExporter, SdlxliffReader sdlxliffReader)
		{
			_window = window;
			_selectedFiles = selectedFiles;
			_commonService = commonService;
			_sdlxliffMerger = sdlxliffMerger;
			_sdlxliffExporter = sdlxliffExporter;
			_sdlxliffReader = sdlxliffReader;

			WindowTitle = PluginResources.StudioViews_SplitSelectedFiles_Name;

			DialogResult = DialogResult.None;
			Reset(null);
		}

		public ICommand OpenFolderInExplorerCommand => _openFolderInExplorerCommand ?? (_openFolderInExplorerCommand = new CommandHandler(OpenFolderInExplorer));

		public ICommand ExportPathBrowseCommand => _exportPathBrowseCommand ?? (_exportPathBrowseCommand = new CommandHandler(ExportPathBrowse));

		public ICommand OkCommand => _okCommand ?? (_okCommand = new CommandHandler(Ok));

		public ICommand ResetCommand => _resetCommand ?? (_resetCommand = new CommandHandler(Reset));

		public string WindowTitle { get; set; }

		public int MaxNumberOfWords
		{
			get => _maxNumberOfWords;
			set
			{
				if (_maxNumberOfWords == value)
				{
					return;
				}

				_maxNumberOfWords = value;
				OnPropertyChanged(nameof(MaxNumberOfWords));
			}
		}

		public int NumberOfEqualParts
		{
			get => _numberOfEqualParts;
			set
			{
				if (_numberOfEqualParts == value)
				{
					return;
				}

				_numberOfEqualParts = value;
				OnPropertyChanged(nameof(NumberOfEqualParts));
			}
		}

		public string SegmentIdsString
		{
			get => _segmentIdsString;
			set
			{
				if (_segmentIdsString == value)
				{
					return;
				}

				_segmentIdsString = value;
				OnPropertyChanged(nameof(SegmentIdsString));
				OnPropertyChanged(nameof(IsValid));
			}
		}

		public bool SplitByWordCount
		{
			get => _splitByWordCount;
			set
			{
				if (_splitByWordCount == value)
				{
					return;
				}

				SplitByEqualParts = false;
				SplitBySegmentIds = false;

				_splitByWordCount = value;
				OnPropertyChanged(nameof(SplitByWordCount));
				OnPropertyChanged(nameof(IsValid));
			}
		}

		public bool SplitByEqualParts
		{
			get => _splitByEqualParts;
			set
			{
				if (_splitByEqualParts == value)
				{
					return;
				}

				SplitByWordCount = false;
				SplitBySegmentIds = false;

				_splitByEqualParts = value;
				OnPropertyChanged(nameof(SplitByEqualParts));
				OnPropertyChanged(nameof(IsValid));
			}
		}

		public bool SplitBySegmentIds
		{
			get => _splitBySegmentIds;
			set
			{
				if (_splitBySegmentIds == value)
				{
					return;
				}

				SplitByWordCount = false;
				SplitByEqualParts = false;

				_splitBySegmentIds = value;
				OnPropertyChanged(nameof(SplitBySegmentIds));
				OnPropertyChanged(nameof(IsValid));
			}
		}

		public bool ProgressIsVisible
		{
			get => _progressIsVisible;
			set
			{
				if (_progressIsVisible == value)
				{
					return;
				}

				_progressIsVisible = value;
				OnPropertyChanged(nameof(ProgressIsVisible));
				OnPropertyChanged(nameof(IsEnabled));
			}
		}

		public bool IsEnabled
		{
			get
			{
				return !ProgressIsVisible;
			}
		}

		public string FileName
		{
			get => _fileName;
			set
			{
				if (_fileName == value)
				{
					return;
				}

				_fileName = value;
				OnPropertyChanged(nameof(FileName));

				var regexNum = new Regex(@"(?<digits>\[[#]+\])", RegexOptions.None);
				FileNameIsValid = !string.IsNullOrEmpty(FileName.Trim()) &&
								  FileName.TrimEnd().EndsWith(".sdlxliff", StringComparison.InvariantCultureIgnoreCase) &&
								  regexNum.Match(FileName).Success;
			}
		}

		public string ExportPath
		{
			get => _exportPath;
			set
			{
				if (_exportPath == value)
				{
					return;
				}

				_exportPath = value;
				OnPropertyChanged(nameof(ExportPath));

				ExportPathIsValid = Directory.Exists(ExportPath);
			}
		}

		public bool FileNameIsValid
		{
			get => _fileNameIsValid;
			set
			{
				if (_fileNameIsValid == value)
				{
					return;
				}

				_fileNameIsValid = value;
				OnPropertyChanged(nameof(FileNameIsValid));
				OnPropertyChanged(nameof(IsValid));
			}
		}

		public bool ExportPathIsValid
		{
			get => _exportPathIsValid;
			set
			{
				if (_exportPathIsValid == value)
				{
					return;
				}

				_exportPathIsValid = value;
				OnPropertyChanged(nameof(ExportPathIsValid));
				OnPropertyChanged(nameof(IsValid));
			}
		}

		public bool IsValid
		{
			get
			{
				var segmentIdsIsValid = true;
				if (SplitBySegmentIds)
				{
					segmentIdsIsValid = !string.IsNullOrEmpty(SegmentIdsString?.Trim());
				}

				return ExportPathIsValid && FileNameIsValid && segmentIdsIsValid;
			}
		}
		
		public DialogResult DialogResult { get; set; }

		public string Message { get; private set; }

		public bool Success { get; private set; }

		public string LogFilePath { get; private set; }

		public DateTime ProcessingDateTime { get; private set; }

		private void ExportPathBrowse(object parameter)
		{
			try
			{
				var initialDirectory = GetValidFolderPath(ExportPath);
				if (string.IsNullOrEmpty(initialDirectory) || !Directory.Exists(initialDirectory))
				{
					initialDirectory = null;
				}

				var folderDialog = new FolderSelectDialog
				{
					Title = "Select the export folder location",
					InitialDirectory = initialDirectory,
				};

				var result = folderDialog.ShowDialog();
				if (result)
				{
					ExportPath = folderDialog.FileName;
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, PluginResources.Plugin_Name, MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}

		private void OpenFolderInExplorer(object parameter)
		{
			if (Directory.Exists(ExportPath))
			{
				Process.Start(ExportPath);
			}
		}

		private void Ok(object parameter)
		{
			if (_selectedFiles == null || _selectedFiles.Count <= 0)
			{
				MessageBox.Show("No files selected!", "Studio Views", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			try
			{
				ProgressIsVisible = true;
				ProcessingDateTime = DateTime.Now;
				LogFilePath = Path.Combine(ExportPath, GetLogFileName("Split", ProcessingDateTime));

				var task = Task.Run(ExportFiles);
				task.ContinueWith(t =>
				{
					Success = t.Result.Success;
					Message = t.Result.Message;

					WriteLogFile(t.Result);

					DialogResult = DialogResult.OK;
					
					AttemptToCloseWindow();
				});
			}
			catch (Exception e)
			{
				DialogResult = DialogResult.Abort;
				ProgressIsVisible = false;
				MessageBox.Show(e.Message, PluginResources.Plugin_Name, MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}


		private string GetLogFileName(string task, DateTime dateTime)
		{
			return "StudioViews_" + task + "Task_"
			       + GetDateTimeToFilePartString(dateTime) + ".log";
		}

		private void WriteLogFile(ExportResult exportResult)
		{
			_window.Dispatcher.Invoke(
				delegate
				{
					using (var sr = new StreamWriter(LogFilePath, false, Encoding.UTF8))
					{
						sr.WriteLine("Studio Views");
						sr.WriteLine("Task: Split Files");
						sr.WriteLine("Start Processing: " + GetDateTimeToString(ProcessingDateTime));

						sr.WriteLine(string.Empty);
						sr.WriteLine("Input Files (" + exportResult.InputFiles.Count + "):");
						var fileIndex = 0;
						foreach (var filePath in exportResult.InputFiles)
						{
							fileIndex++;
							sr.WriteLine("  File (" + fileIndex + "): " + filePath);
						}

						sr.WriteLine(string.Empty);
						sr.WriteLine("Output Files (" + exportResult.OutputFiles.Count + "):");
						fileIndex = 0;
						foreach (var filePath in exportResult.OutputFiles)
						{
							fileIndex++;
							sr.WriteLine("  File (" + fileIndex + "): " + filePath);
						}

						sr.WriteLine(string.Empty);
						sr.WriteLine("End Processing: " + GetDateTimeToString(DateTime.Now));

						sr.Flush();
						sr.Close();
					}
				});
		}

		private string GetDateTimeToFilePartString(DateTime dateTime)
		{
			var value = (dateTime != DateTime.MinValue && dateTime != DateTime.MaxValue)
				? dateTime.Year
				  + "" + dateTime.Month.ToString().PadLeft(2, '0')
				  + "" + dateTime.Day.ToString().PadLeft(2, '0')
				  + "T" + dateTime.Hour.ToString().PadLeft(2, '0')
				  + "" + dateTime.Minute.ToString().PadLeft(2, '0')
				  + "" + dateTime.Second.ToString().PadLeft(2, '0')
				: "none";
			return value;
		}
		
		private string GetDateTimeToString(DateTime dateTime)
		{
			var value = (dateTime != DateTime.MinValue && dateTime != DateTime.MaxValue)
				? dateTime.Year
				  + "-" + dateTime.Month.ToString().PadLeft(2, '0')
				  + "-" + dateTime.Day.ToString().PadLeft(2, '0')
				  + " " + dateTime.Hour.ToString().PadLeft(2, '0')
				  + ":" + dateTime.Minute.ToString().PadLeft(2, '0')
				  + ":" + dateTime.Second.ToString().PadLeft(2, '0')
				: "[none]";
			return value;
		}

		private void Reset(object paramter)
		{
			ProgressIsVisible = false;
			SplitByWordCount = true;
			MaxNumberOfWords = 1000;
			NumberOfEqualParts = 2;

			SetDefaultOutputValues();
			OnPropertyChanged(nameof(IsValid));
		}

		private async Task<ExportResult> ExportFiles()
		{
			var filePathInput = _selectedFiles[0].LocalFilePath;

			var exportResult = new ExportResult
			{
				InputFiles = _selectedFiles.Select(a => a.LocalFilePath).ToList(),
				OutputFiles = new List<string>()
			};

			if (_selectedFiles.Count > 1)
			{
				var files = _selectedFiles.Select(a => a.LocalFilePath).ToList();
				var fileDirectory = Path.GetDirectoryName(files[0]);
				var filePathOutput =
					_commonService.GetUniqueFileName(Path.Combine(fileDirectory, "StudioViewsFile.sdlxliff"), string.Empty);

				var mergedFile = _sdlxliffMerger.MergeFiles(files, filePathOutput, false);
				if (mergedFile)
				{
					filePathInput = filePathOutput;
				}
				else
				{
					exportResult.Success = false;
					exportResult.Message = "Unexpected error while merging files.";
					return await Task.FromResult(exportResult);
				}
			}
			
			var segmentPairs = _sdlxliffReader.GetSegmentPairs(filePathInput);
			
			var segmentPairSplits = GetSegmentPairSplits(segmentPairs);
			if (segmentPairSplits == null)
			{
				exportResult.Success = false;
				exportResult.Message = "No segments available.";
				return await Task.FromResult(exportResult);
			}

			var fileIndex = 0;
			foreach (var segmentPairSplit in segmentPairSplits)
			{
				fileIndex++;
				var filePathOutput = GetFilePathOutput(FileName, ExportPath, fileIndex);
				_sdlxliffExporter.ExportFile(segmentPairSplit, filePathInput, filePathOutput);

				exportResult.OutputFiles.Add(filePathOutput);
			}

			if (_selectedFiles.Count > 1)
			{
				if (File.Exists(filePathInput))
				{
					File.Delete(filePathInput);
				}
			}

			exportResult.Success = true;
			exportResult.Message = Message = "Successfully completed the split operation.\r\n\r\n";
			exportResult.Message += string.Format("Exported {0} segments into {1} separate files", segmentPairs.Count, fileIndex);
			
			return await Task.FromResult(exportResult);
		}

		private IEnumerable<List<SegmentPairInfo>> GetSegmentPairSplits(List<SegmentPairInfo> segmentPairs)
		{
			IEnumerable<List<SegmentPairInfo>> segmentPairSplits = null;
			if (SplitByWordCount)
			{
				segmentPairSplits = GetSegmentPairsSplitByMaxWordCount(segmentPairs, MaxNumberOfWords);
			}
			else if (SplitByEqualParts)
			{
				var splitWordCount = GetTotalWordCount(segmentPairs);
				var maxNumberOfWords =
					(int)Math.Round(Convert.ToDecimal(splitWordCount) / Convert.ToDecimal(NumberOfEqualParts), 0,
						MidpointRounding.AwayFromZero);
				segmentPairSplits = GetSegmentPairsSplitByMaxWordCount(segmentPairs, maxNumberOfWords);
			}
			else if (SplitBySegmentIds)
			{
				var segmentIds = GetSegmentIds(SegmentIdsString);
				segmentPairSplits = GetSegmentPairsSplitBySegmentId(segmentPairs, segmentIds);
			}

			return segmentPairSplits;
		}

		private IEnumerable<List<SegmentPairInfo>> GetSegmentPairsSplitBySegmentId(IEnumerable<SegmentPairInfo> segmentPairs, List<string> segmentIds)
		{
			var segmentPairsSplits = new List<List<SegmentPairInfo>>();

			var segmentPairSplits = new List<SegmentPairInfo>();
			foreach (var segmentPair in segmentPairs)
			{
				segmentPairSplits.Add(segmentPair);

				if (segmentIds.Exists(a => string.Compare(a.Trim(),
					segmentPair.SegmentId, StringComparison.InvariantCultureIgnoreCase) == 0))
				{
					segmentPairsSplits.Add(segmentPairSplits);
					segmentPairSplits = new List<SegmentPairInfo>();
				}
			}

			if (segmentPairSplits.Count > 0)
			{
				segmentPairsSplits.Add(segmentPairSplits);
			}

			return segmentPairsSplits;
		}

		private IEnumerable<List<SegmentPairInfo>> GetSegmentPairsSplitByMaxWordCount(IEnumerable<SegmentPairInfo> segmentPairs, int maxWordCount)
		{
			var segmentPairsSplits = new List<List<SegmentPairInfo>>();

			var splitWordCount = 0;
			var segmentPairSplits = new List<SegmentPairInfo>();
			foreach (var segmentPair in segmentPairs)
			{
				segmentPairSplits.Add(segmentPair);
				splitWordCount += segmentPair.SourceWordCounts.Words;

				if (splitWordCount >= maxWordCount)
				{
					segmentPairsSplits.Add(segmentPairSplits);

					splitWordCount = 0;
					segmentPairSplits = new List<SegmentPairInfo>();
				}
			}

			if (segmentPairSplits.Count > 0)
			{
				segmentPairsSplits.Add(segmentPairSplits);
			}

			return segmentPairsSplits;
		}

		private void AttemptToCloseWindow()
		{
			var task = Task.Run(
				delegate
				{
					System.Threading.Thread.Sleep(500);
				});

			task.ContinueWith(
				delegate
				{
					ProgressIsVisible = false;
					_window.Dispatcher.BeginInvoke(
						new Action(delegate
						{
							_window?.Close();
						}));
				}
			);
		}

		private List<string> GetSegmentIds(string segmentIdsString)
		{
			var segmentIds = segmentIdsString.Replace(" ", "").Trim().Split(',').ToList();
			return segmentIds;
		}

		private int GetTotalWordCount(IEnumerable<SegmentPairInfo> segmentPairs)
		{
			return segmentPairs.Sum(segmentPair => segmentPair.SourceWordCounts.Words);
		}

		private string GetFilePathOutput(string fileName, string fileFolder, int fileIndex)
		{
			var regexNum = new Regex(@"(?<digits>\[[#]+\])", RegexOptions.None);
			var fileNameOutput = fileName;
			var match = regexNum.Match(fileNameOutput);
			if (match.Success)
			{
				var digits = match.Groups["digits"].Name.Length - 2;
				var fileNamePrefix = fileNameOutput.Substring(0, match.Index);
				var digitsString = fileIndex.ToString().PadLeft(digits, '0');
				var fileNameSuffix = fileNameOutput.Substring(match.Index + match.Length);

				fileNameOutput = fileNamePrefix + digitsString + fileNameSuffix;
			}

			var filePathOutput = Path.Combine(fileFolder, fileNameOutput);
			return filePathOutput;
		}

		private void SetDefaultOutputValues()
		{
			if (_selectedFiles?.Count > 0)
			{
				ExportPath = Path.GetDirectoryName(_selectedFiles[0].LocalFilePath);
				if (_selectedFiles.Count == 1)
				{
					var name = Path.GetFileName(_selectedFiles[0].LocalFilePath);
					FileName = name.Substring(0, name.LastIndexOf(".sdlxliff", StringComparison.CurrentCultureIgnoreCase));
					FileName += ".Split_[####].sdlxliff";
				}
				else
				{
					FileName = "StudioViewsFile.Split_[####].sdlxliff";
				}
			}
		}

		private string GetValidFolderPath(string initialPath)
		{
			if (string.IsNullOrWhiteSpace(initialPath))
			{
				return string.Empty;
			}

			var outputFolder = initialPath;
			if (Directory.Exists(outputFolder))
			{
				return outputFolder;
			}

			while (outputFolder.Contains("\\"))
			{
				outputFolder = outputFolder.Substring(0, outputFolder.LastIndexOf("\\", StringComparison.Ordinal));
				if (Directory.Exists(outputFolder))
				{
					return outputFolder;
				}
			}

			return outputFolder;
		}
	}
}
