﻿using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sdl.Community.XLIFF.Manager.Model
{
	public class ProjectModel : BaseModel
	{
		public string Id { get; set; }

		public string Name { get; set; }

		public string Path { get; set; }

		public string ClientName { get; set; }

		public DateTime DueDate { get; set; }

		public DateTime Created { get; set; }

		public string ProjectType { get; set; }

		public CultureInfo SourceLanguage { get; set; }

		public List<CultureInfo> TargetLanguages { get; set; }

		public List<ProjectFileActionModel> ProjectFileActionModels { get; set; }
	}
}
