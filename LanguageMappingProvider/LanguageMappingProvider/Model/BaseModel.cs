﻿using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LanguageMappingProvider;

public class BaseModel : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler PropertyChanged;

	protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}