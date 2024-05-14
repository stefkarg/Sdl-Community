﻿using Sdl.TellMe.ProviderApi;

namespace Sdl.Community.SdlDataProtectionSuite.TellMe
{
	[TellMeProvider]
	public class TellMeProvider : ITellMeProvider
	{
		public TellMeProvider()
		{
			ProviderActions = GetProviderActions();
		}

		public string Name => PluginResources.Plugin_Name;

		public AbstractTellMeAction[] ProviderActions =>
		[
			new DocumentationAction
			{
				Keywords = ["data", "protection", "suite", "community", "support", "wiki"]
			},
			new AppStoreForumAction
			{
				Keywords = ["data", "protection", "suite", "support", "forum"]
			},
			new SourceCodeAction
			{
				Keywords = ["data", "protection", "suite", "source", "code"]
			},
			new SettingsAction
			{
				Keywords = ["data", "protection", "suite", "settings", "tm", "anonymizer"]
			}
		];
	}
}
