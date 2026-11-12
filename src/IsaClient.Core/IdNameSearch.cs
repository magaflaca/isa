using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;

namespace IsaClient.Core;

/// <summary>
/// Name lookup over an ID class' public constants. Unlike ReLogic's IdDictionary it tolerates duplicate values
/// (PaintID has IlluminantPaint / Old_IlluminantPaint both equal to 31): the first declared name wins.
/// </summary>
public sealed class IdNameSearch
{
	private readonly Dictionary<int, string> _names = new Dictionary<int, string>();

	private readonly Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public int Count => _names.Count;

	public static IdNameSearch Create(Type idClass, Type idType)
	{
		IdNameSearch search = new IdNameSearch();
		foreach (FieldInfo field in idClass.GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.IsLiteral && field.FieldType == idType)
			{
				int id = Convert.ToInt32(field.GetValue(null));
				if (!search._names.ContainsKey(id))
				{
					search._names[id] = field.Name;
				}
				if (!search._ids.ContainsKey(field.Name))
				{
					search._ids[field.Name] = id;
				}
			}
		}
		return search;
	}

	public string GetName(int id)
	{
		if (_names.TryGetValue(id, out var value))
		{
			return value;
		}
		return "Unknown (" + id + ")";
	}

	public bool TryGetName(int id, out string name)
	{
		return _names.TryGetValue(id, out name);
	}

	public bool TryGetId(string name, out int id)
	{
		return _ids.TryGetValue(name, out id);
	}

	public int GetId(string name)
	{
		return _ids[name];
	}

	public bool ContainsName(string name)
	{
		return _ids.ContainsKey(name);
	}

	public bool ContainsId(int id)
	{
		return _names.ContainsKey(id);
	}
}
