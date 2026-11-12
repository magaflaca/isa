using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace IsaClient.Plugins;

/// <summary>
/// Harmony patches made by plugins. When a plugin is turned off or reloaded, every patch whose code lives in its
/// assembly is removed, whatever Harmony id it used, so a plugin can't leave the game modified after it is gone.
/// </summary>
public static class PluginPatches
{
	/// <summary>Removes the prefixes, postfixes, transpilers and finalizers defined in <paramref name="assembly"/>.</summary>
	public static int RemovePatchesOf(Assembly assembly)
	{
		int removed = 0;
		foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
		{
			Patches info = Harmony.GetPatchInfo(original);
			if (info == null)
			{
				continue;
			}
			IEnumerable<Patch> patches = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers);
			foreach (Patch patch in patches.Where(p => p.PatchMethod?.DeclaringType?.Assembly == assembly).ToList())
			{
				try
				{
					new Harmony(patch.owner).Unpatch(original, patch.PatchMethod);
					removed++;
				}
				catch (Exception ex)
				{
					PluginLog.Error("Could not remove a patch of " + original.Name + ": " + ex.Message, ex);
				}
			}
		}
		return removed;
	}

	/// <summary>How many game methods <paramref name="assembly"/> patches right now.</summary>
	public static int CountPatchedBy(Assembly assembly)
	{
		int count = 0;
		foreach (MethodBase original in Harmony.GetAllPatchedMethods())
		{
			Patches info = Harmony.GetPatchInfo(original);
			if (info != null && info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Any(p => p.PatchMethod?.DeclaringType?.Assembly == assembly))
			{
				count++;
			}
		}
		return count;
	}
}
