﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using InterpretBank.SettingsService.ViewModel.Interface;

namespace InterpretBank.SettingsService.ViewModel
{
	public class ViewModel : IViewModel
	{
		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
		{
			if (EqualityComparer<T>.Default.Equals(field, value))
				return false;
			field = value;
			OnPropertyChanged(propertyName);
			return true;
		}
	}
}