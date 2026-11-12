using System;

namespace IsaClient.Core;

/// <summary>Marks a member with a short description.</summary>
public sealed class InfoAttribute : Attribute
{
	private string _message;

	public string Message => _message;

	public InfoAttribute()
	{
		_message = "";
	}

	public InfoAttribute(string message)
	{
		this._message = message;
	}
}
