﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Sdl.Community.XLIFF.Manager.Common;
using Sdl.Community.XLIFF.Manager.Controls;
using Sdl.Community.XLIFF.Manager.Model;
using Sdl.Community.XLIFF.Manager.Service;
using Sdl.Community.XLIFF.Manager.ViewModel;
using Sdl.Desktop.IntegrationApi;
using Sdl.Desktop.IntegrationApi.Extensions;
using Sdl.Desktop.IntegrationApi.Interfaces;
using Sdl.Desktop.IntegrationApi.Notifications.Events;
using Sdl.TranslationStudioAutomation.IntegrationApi;
using Sdl.TranslationStudioAutomation.IntegrationApi.Presentation.DefaultLocations;

namespace Sdl.Community.XLIFF.Manager
{
	[View(
		Id = "XLIFFManager_View",
		Name = "XLIFFManager_Name",
		Description = "XLIFFManager_Description",
		Icon = "Icon",
		AllowViewParts = true,
		LocationByType = typeof(TranslationStudioDefaultViews.TradosStudioViewsLocation))]
	public class XLIFFManagerViewController : AbstractViewController
	{
		private readonly object _lockObject = new object();
		private List<Project> _projects;
		private ProjectFilesViewModel _projectFilesViewModel;
		private ProjectsNavigationViewModel _projectsNavigationViewModel;
		private ProjectFilesViewControl _projectFilesViewControl;
		private ProjectsNavigationViewControl _projectsNavigationViewControl;
		private ProjectFileActivityViewController _projectFileActivityViewController;
		private IStudioEventAggregator _eventAggregator;
		private ProjectsController _projectsController;
		private ImageService _imageService;
		private PathInfo _pathInfo;

		protected override void Initialize(IViewContext context)
		{
			_pathInfo = new PathInfo();
			_imageService = new ImageService(_pathInfo);
			_imageService.ExtractFlags();

			ActivationChanged += OnActivationChanged;

			_eventAggregator = SdlTradosStudio.Application.GetService<IStudioEventAggregator>();
			_eventAggregator.GetEvent<StudioWindowCreatedNotificationEvent>()?.Subscribe(OnStudioWindowCreatedNotificationEvent);

			_projectsController = SdlTradosStudio.Application.GetController<ProjectsController>();

			// TODO this will be replaced with a call to recover the relevant data from the projects loaded in Studio			
			//var testDataUtil = new TestDataUtil(_imageService);
			//_projectModels = testDataUtil.GetTestProjectData();
		}

		protected override Control GetExplorerBarControl()
		{
			if (_projectsNavigationViewControl == null)
			{
				_projectsNavigationViewModel = new ProjectsNavigationViewModel(_projects);
				_projectsNavigationViewControl = new ProjectsNavigationViewControl(_projectsNavigationViewModel);
			}

			return _projectsNavigationViewControl;
		}

		protected override Control GetContentControl()
		{
			if (_projectFilesViewControl == null)
			{
				_projectFilesViewModel = new ProjectFilesViewModel(_projects?.Count > 0 ? _projects[0].ProjectFiles : null);
				_projectFilesViewControl = new ProjectFilesViewControl(_projectFilesViewModel);

				_projectsNavigationViewModel.ProjectFilesViewModel = _projectFilesViewModel;
			}

			return _projectFilesViewControl;
		}

		public List<Project> GetSelectedProjects()
		{
			return _projectsNavigationViewModel.SelectedProjects?.Cast<Project>().ToList();
		}

		public List<ProjectFile> GetSelectedProjectFiles()
		{
			return _projectFilesViewModel.SelectedProjectFiles?.Cast<ProjectFile>().ToList();
		}

		public void UpdateProjectData(WizardContext wizardContext)
		{
			if (wizardContext == null || !wizardContext.Completed)
			{
				return;
			}

			if (_projects == null)
			{
				_projects = new List<Project>();
			}

			var project = _projects.FirstOrDefault(a => a.Id == wizardContext.Project.Id);
			if (project == null)
			{
				_projects.Add(wizardContext.Project);
				project = wizardContext.Project;
			}
			else
			{
				foreach (var wcProjectFile in wizardContext.ProjectFiles)
				{
					var projectFile = project.ProjectFiles.FirstOrDefault(a => a.Id == wcProjectFile.Id);
					if (projectFile == null)
					{
						project.ProjectFiles.Add(wcProjectFile);
					}
					else
					{
						// TODO
					}
				}
			}

			_projectsNavigationViewModel.Projects = _projects;
			_projectFilesViewModel.ProjectFiles = project.ProjectFiles;			
		}

		private void OnStudioWindowCreatedNotificationEvent(StudioWindowCreatedNotificationEvent e)
		{

		}

		private void OnActivationChanged(object sender, ActivationChangedEventArgs e)
		{
			if (e.Active)
			{
				SetProjectFileActivityViewController();
			}
		}

		private void SetProjectFileActivityViewController()
		{
			lock (_lockObject)
			{
				if (_projectFileActivityViewController != null)
				{
					return;
				}

				try
				{
					_projectFileActivityViewController =
						SdlTradosStudio.Application.GetController<ProjectFileActivityViewController>();

					_projectFilesViewModel.ProjectFileActivityViewModel =
						new ProjectFileActivityViewModel(_projectFilesViewModel?.SelectedProjectFile?.ProjectFileActivities);

					_projectFileActivityViewController.ViewModel = _projectFilesViewModel.ProjectFileActivityViewModel;
				}
				catch
				{
					// catch all; unable to locate the controller
				}
			}
		}

		public override void Dispose()
		{
			_projectFilesViewModel?.Dispose();
			_projectsNavigationViewModel?.Dispose();

			base.Dispose();
		}
	}
}
