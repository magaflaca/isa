namespace Terraria.GameInput;

public partial class TriggersSet
{
	public bool KeyStatusHandler(string key, bool? value = null)
	{
		if (KeyStatus.ContainsKey(key))
		{
			if (value.HasValue)
			{
				KeyStatus[key] = value.Value;
			}
			return KeyStatus[key];
		}
		return false;
	}
}
