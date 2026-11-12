using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria.IO;

namespace IsaClient.Core;

/// <summary>Reads and writes every static field marked with <see cref="SettingAttribute"/>.</summary>
internal static class SettingFields
{
	private sealed class Entry
	{
		public FieldInfo Field;

		public SettingAttribute Setting;
	}

	private static List<Entry> _entries;

	private static readonly MethodInfo GetMethod = typeof(Preferences).GetMethods()
		.Single(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);

	private static List<Entry> Entries
	{
		get
		{
			if (_entries == null)
			{
				_entries = LoadableTypes()
					.SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
					.Select(info => new Entry { Field = info, Setting = info.GetCustomAttribute<SettingAttribute>() })
					.Where(entry => entry.Setting != null)
					.OrderBy(entry => entry.Setting.Key, StringComparer.Ordinal)
					.ToList();
			}
			return _entries;
		}
	}

	/// <summary>Every type of the client that can be loaded (types that need a missing library are skipped).</summary>
	private static IEnumerable<Type> LoadableTypes()
	{
		try
		{
			return typeof(SettingFields).Assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where(t => t != null);
		}
	}

	/// <summary>Loads the settings of one kind: <paramref name="remembered"/> false for normal settings, true for cheat toggles.</summary>
	public static void Read(Preferences store, bool remembered)
	{
		foreach (Entry entry in Entries.Where(e => e.Setting.Remembered == remembered))
		{
			string key = (!store.Contains(entry.Setting.Key) && entry.Setting.LegacyKey != null && store.Contains(entry.Setting.LegacyKey)) ? entry.Setting.LegacyKey : entry.Setting.Key;
			if (!store.Contains(key))
			{
				continue;
			}
			try
			{
				object current = entry.Field.GetValue(null);
				object value = entry.Field.FieldType.IsEnum
					? ParseEnum(entry.Field.FieldType, store.Get(key, current.ToString()), current)
					: GetMethod.MakeGenericMethod(entry.Field.FieldType).Invoke(store, new object[] { key, current });
				entry.Field.SetValue(null, Clamp(value, entry.Setting));
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
	}

	/// <summary>Writes every setting (cheat toggles included).</summary>
	public static void Write(Preferences store)
	{
		foreach (Entry entry in Entries)
		{
			// a switch a server turned off for now: its own value is saved, not the forced one
			object value = IsaClient.Networking.ServerModes.TryGetKept(entry.Field, out object kept) ? kept : entry.Field.GetValue(null);
			store.Put(entry.Setting.Key, entry.Field.FieldType.IsEnum ? value.ToString() : value);
		}
	}

	private static object ParseEnum(Type type, string text, object fallback)
	{
		try
		{
			return Enum.Parse(type, text, ignoreCase: true);
		}
		catch
		{
			return fallback;
		}
	}

	private static object Clamp(object value, SettingAttribute setting)
	{
		if (double.IsNaN(setting.Min) && double.IsNaN(setting.Max))
		{
			return value;
		}
		switch (value)
		{
		case int i:
			return (int)Math.Max(double.IsNaN(setting.Min) ? int.MinValue : setting.Min, Math.Min(double.IsNaN(setting.Max) ? int.MaxValue : setting.Max, i));
		case float f:
			return (float)Math.Max(double.IsNaN(setting.Min) ? float.MinValue : setting.Min, Math.Min(double.IsNaN(setting.Max) ? float.MaxValue : setting.Max, f));
		case double d:
			return Math.Max(double.IsNaN(setting.Min) ? double.MinValue : setting.Min, Math.Min(double.IsNaN(setting.Max) ? double.MaxValue : setting.Max, d));
		default:
			return value;
		}
	}
}
