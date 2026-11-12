using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.Building;

/// <summary>Undo history of pastes (.undo / Ctrl+Z): the area under each paste is captured before it is changed.</summary>
public static class PasteUndo
{
	public sealed class PasteUndoEntry
	{
		public Point Position;

		public int SizeX;

		public int SizeY;

		public List<TileCopy> Tiles;
	}

	private const int MaxUndoEntries = 10;

	private const long MaxUndoTiles = 4000000L;

	private static readonly List<PasteUndoEntry> _undoStack = new List<PasteUndoEntry>();

	public static int UndoCount => _undoStack.Count;

	/// <summary>Snapshot of the area a paste is about to overwrite, in the same x-major order the paste reads.</summary>
	internal static PasteUndoEntry CaptureRegion(Point origin, int sizeX, int sizeY)
	{
		PasteUndoEntry entry = new PasteUndoEntry
		{
			Position = origin,
			SizeX = sizeX,
			SizeY = sizeY,
			Tiles = new List<TileCopy>(sizeX * sizeY)
		};
		for (int x = origin.X; x < origin.X + sizeX; x++)
		{
			for (int y = origin.Y; y < origin.Y + sizeY; y++)
			{
				Tile tile = (WorldGen.InWorld(x, y) ? Main.tile[x, y] : null);
				entry.Tiles.Add((tile != null) ? new TileCopy(tile) : new TileCopy());
			}
		}
		return entry;
	}

	internal static void PushUndo(PasteUndoEntry entry)
	{
		lock (_undoStack)
		{
			_undoStack.Add(entry);
			while (_undoStack.Count > MaxUndoEntries || (_undoStack.Count > 1 && _undoStack.Sum((PasteUndoEntry e) => (long)e.Tiles.Count) > MaxUndoTiles))
			{
				_undoStack.RemoveAt(0);
			}
		}
	}

	/// <summary>
	/// Ctrl+Z / .undo: cancels a paste that is still running and pastes back what was there before, through the same
	/// code path (and the same TShock mode) as a normal paste, with air blending off so removed blocks come back empty.
	/// </summary>
	public static void UndoLastPaste()
	{
		PasteUndoEntry entry;
		lock (_undoStack)
		{
			if (_undoStack.Count == 0)
			{
				ClientChat.Error("Nothing to undo.");
				return;
			}
			entry = _undoStack[_undoStack.Count - 1];
			_undoStack.RemoveAt(_undoStack.Count - 1);
		}
		bool running = Selection.ConcurrentPastes > 0;
		if (running)
		{
			Selection.CancelPaste = true;
		}
		ClientChat.Info("Undoing paste at X: " + entry.Position.X + ", Y: " + entry.Position.Y + " (" + entry.SizeX + "x" + entry.SizeY + ")" + (running ? ", stopping the current paste first..." : "..."));
		Task.Run(delegate
		{
			for (int i = 0; i < 300 && Selection.ConcurrentPastes > 0; i++)
			{
				Thread.Sleep(50);
			}
			if (Selection.ConcurrentPastes > 0)
			{
				Selection.ConcurrentPastes = 0;
			}
			Thread.Sleep(150);
			Main.QueueMainThreadAction(delegate
			{
				// Multi Paste on: the bots undo it with you, each its strip.
				if (!IsaClient.MultiClient.MultiPaste.TryStartUndo(entry))
				{
					PasteEngine.PasteTilesInternal(entry.Position, entry);
				}
			});
		});
	}
}
