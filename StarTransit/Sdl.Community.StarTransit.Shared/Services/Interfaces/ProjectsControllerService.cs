﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sdl.TranslationStudioAutomation.IntegrationApi;

namespace Sdl.Community.StarTransit.Shared.Services.Interfaces
{
	public class ProjectsControllerService : IProjectsControllerService
	{
		private readonly ProjectsController _projectsController;

		public ProjectsControllerService(ProjectsController projectsController)
		{
			_projectsController = projectsController;
		}

		public void RefreshProjects()
		{
			_projectsController.RefreshProjects();
		}
	}
}
