using System;
using System.Collections.Generic;

namespace IsaClient.Gba.Trainer;

/// <summary>What a conversion produced: the patched ROM and the pictures the client shows (preview, picker).</summary>
public sealed class TrainerResult
{
	public byte[] Rom;

	public bool RedBody;

	/// <summary>Overworld frames ("normal" walk/run set first), indexed in <see cref="Palette"/>.</summary>
	public List<byte[,]> Frames;

	public List<string> Kinds;

	public Rgb[] Palette;

	public List<byte[,]> Back;

	public Rgb[] BackPalette;

	public byte[,] Front;

	public Rgb[] FrontPalette;
}

/// <summary>
/// Turns a Terraria character into the FireRed/LeafGreen player (overworld, battle back, trainer card): the C# port of
/// terraria-to-pokemon's convert() without helmets. Only the in-memory copy of the ROM changes.
/// </summary>
public static class TrainerBuilder
{
	/// <param name="body">null = by gender (Red for men, Leaf for women), true Red, false Leaf.</param>
	public static TrainerResult Build(byte[] originalRom, TrainerLook look, bool? body = null)
	{
		byte[] data = (byte[])originalRom.Clone();
		FireRedRom rom = new FireRedRom(data);
		if (!TrainerPics.FreeSpace(data))
		{
			throw new InvalidOperationException("This ROM has no free space at 0xFE0000 (a ROM hack?)");
		}
		TrainerColors colors = new TrainerColors(look);
		HeadSet heads = HeadAtlas.Get(look.Hair, look.Male);
		Rgba[][,] painted = { heads.Down.Paint(colors), heads.Up.Paint(colors), heads.Left.Paint(colors), heads.Right.Paint(colors) };
		bool red = body ?? look.Male;
		TrainerGame game = rom.Game;
		List<BodyFrame> frames = BodyTemplates.Load(rom, red);
		BodyTemplates.Analyse(frames, game, red);
		var tags = game.PaletteTags[(red || game.PaletteTags.Length == 1) ? 0 : 1];
		Rgb[] origPal = rom.ReadPalette(tags.Player);
		Rgb[] origRefl = rom.ReadPalette(tags.Reflection);
		List<Rgba[,]> images = frames.ConvertAll(f => TrainerCompose.Compose(f, origPal, heads, colors, painted));
		var (pal, indexed) = TrainerCompose.Quantize(images, colors.Protected());
		// the same character goes in Red's and Leaf's slots so either save works
		foreach (bool target in new[] { true, false })
		{
			List<BodyFrame> slots = BodyTemplates.Load(rom, target);
			for (int i = 0; i < slots.Count && i < indexed.Count; i++)
			{
				rom.WriteFrame(slots[i].Off, indexed[i]);
			}
		}
		Rgb[] refl = TrainerCompose.ReflectionPalette(pal, origPal, origRefl);
		foreach (var (player, reflection) in game.PaletteTags)
		{
			rom.WritePalette(player, pal);
			rom.WritePalette(reflection, refl);
		}
		TrainerPics pics = new TrainerPics(rom);
		var (back, front) = pics.Build(heads, colors, red);
		var (backPal, backIdx) = TrainerCompose.Quantize(back, colors.Protected());
		pics.Write(true, backIdx, backPal);
		var (frontPal, frontIdx) = TrainerCompose.Quantize(front, colors.Protected());
		pics.Write(false, frontIdx, frontPal);
		return new TrainerResult
		{
			Rom = data,
			RedBody = red,
			Frames = indexed,
			Kinds = frames.ConvertAll(f => f.Kind),
			Palette = pal,
			Back = backIdx,
			BackPalette = backPal,
			Front = frontIdx[0],
			FrontPalette = frontPal
		};
	}
}
