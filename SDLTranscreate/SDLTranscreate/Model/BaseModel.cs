﻿using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Trados.Transcreate.Model
{
	public class BaseModel: INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			if (propertyName != null)
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			}
		}
	}
}
