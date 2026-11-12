using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Feedback;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.OS;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Report a Bug: send bugs, suggestions and feedback to the developers (New Report), automatic crash reports and the
/// crashes kept here (Crashes), and everything sent with its status (My Reports).
/// </summary>
internal static class HubFeedback
{
	private const int ChipHeight = 28;

	private const float BodyScale = 0.78f;

	private const int LineHeight = 20;

	private static ReportKind _kind = ReportKind.Bug;

	private static string _title = "";

	private static string _text = "";

	private static string _contact;

	private static int _severity = -1;

	private static int _frequency = -1;

	private static int _area = -1;

	private static bool _withInfo = true;

	private static bool _withErrors = true;

	private static string _message;

	private static bool _messageIsError;

	private static DateTime _messageTime;

	private static FeedbackReport _justSent;

	private static int _filter;

	private static readonly string[] Severities = { "Small detail", "Annoying", "Breaks the game" };

	private static readonly string[] Frequencies = { "Every time", "Sometimes", "Only once" };

	private static readonly string[] Areas = { "Panel", "Commands", "Player & Combat", "Building", "World", "Multiplayer", "Music", "AI", "Plugins", "Other" };

	private static HubTheme Theme => HubTheme.Current;

	/// <summary>Picks the kind of the next report (.bug, .suggest).</summary>
	public static void Pick(ReportKind kind)
	{
		if (kind != ReportKind.Crash)
		{
			_kind = kind;
		}
	}

	/// <summary>"3 sent, crash reports on" for a card.</summary>
	public static string Summary()
	{
		int sent = FeedbackReports.History.Count(r => r.Kind != ReportKind.Crash && r.Status == ReportStatus.Sent);
		return sent + " sent, crash reports " + (FeedbackReports.AutoCrashReports ? "on" : "off");
	}

	#region New report

	public static int DrawReport(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Hero(sb, x, y, width, HubIcon.Item(I.Buggy), "Help make IsaClient better", "Found something broken, or have an idea? Tell us here: it goes straight to the developers, and every report is read.", FeedbackReport.KindColor(_kind));
		y += 14;

		y += Label(sb, "What is it?", x, y);
		y += KindTiles(sb, x, y, width) + 14;

		y += Label(sb, "Title", x, y, _title.Length + " / " + FeedbackReports.TitleLimit, width);
		_title = HubKit.Field(sb, "fb:title", new Rectangle(x, y, width, 34), _title, TitleHint(), FeedbackReports.TitleLimit, HubIcon.Item(FeedbackReport.KindIcon(_kind)));
		y += 44;

		y += Label(sb, (_kind == ReportKind.Bug) ? "What happened?" : "Tell us more", x, y, _text.Length + " / " + FeedbackReports.TextLimit, width);
		y += TextArea(sb, "fb:text", x, y, width, ref _text, DetailsHint(), FeedbackReports.TextLimit) + 12;

		if (_kind == ReportKind.Bug)
		{
			y += ChipRow(sb, "fb:sev", "How bad is it?", x, y, width, Severities, ref _severity);
			y += ChipRow(sb, "fb:freq", "How often?", x, y, width, Frequencies, ref _frequency);
		}
		y += ChipRow(sb, "fb:area", "Part of the client", x, y, width, Areas, ref _area) + 4;

		y += Label(sb, "Your Discord (optional)", x, y);
		_contact ??= FeedbackReports.Contact ?? "";
		Rectangle contactField = new Rectangle(x, y, Math.Min(width, 360), 32);
		_contact = HubKit.Field(sb, "fb:contact", contactField, _contact, "So we can ask you about it", FeedbackReports.ContactLimit, HubIcon.Item(I.Megaphone));
		HubKit.Text(sb, "Only if you want an answer.", contactField.Right + 10, y + 8, Theme.Muted * 0.85f, 0.68f, x + width - contactField.Right - 10);
		y += 44;

		int errors = ClientLog.Recent.Count;
		y += Toggles(sb, x, y, width, new (string, string, string, bool)[]
		{
			("fb:t:info", "Include game & PC info", "Client version, where you are (menu, singleplayer, multiplayer), Windows version, screen size, memory and loaded plugins. Helps a lot to find bugs.", _withInfo),
			("fb:t:err", "Include recent errors" + (errors > 0 ? " (" + errors + ")" : ""), (errors > 0) ? ("The " + errors + " error" + (errors == 1 ? "" : "s") + " IsaClient logged since the game started.") : "No errors logged since the game started.", _withErrors)
		}, out int flipped);
		if (flipped == 0)
		{
			_withInfo = !_withInfo;
		}
		else if (flipped == 1)
		{
			_withErrors = !_withErrors;
		}
		y += 8;

		if (_withInfo)
		{
			y += InfoBox(sb, x, y, width) + 12;
		}

		y += SendBar(sb, x, y, width);
		return y - start;
	}

	private static string TitleHint()
	{
		return _kind switch
		{
			ReportKind.Bug => "Short summary: \"Freecam stops working after dying\"",
			ReportKind.Suggestion => "Your idea in a few words: \"Waypoints on the map\"",
			_ => "What is it about?"
		};
	}

	private static string DetailsHint()
	{
		return _kind switch
		{
			ReportKind.Bug => "What did you do, what happened, and what did you expect? Steps to make it happen again help the most. (Enter = new line)",
			ReportKind.Suggestion => "Describe the feature or the change: what it does, how you would use it, where it would go in the panel...",
			_ => "Anything you want to tell the developers."
		};
	}

	private static int KindTiles(SpriteBatch sb, int x, int y, int width)
	{
		(ReportKind Kind, string Title, string Line)[] kinds =
		{
			(ReportKind.Bug, "Bug", "Something is broken or wrong"),
			(ReportKind.Suggestion, "Suggestion", "A new feature or an improvement"),
			(ReportKind.Feedback, "Feedback", "Anything else you want to say")
		};
		int columns = (width >= 540) ? 3 : 1;
		int tileW = (width - (columns - 1) * 10) / columns;
		const int height = 62;
		for (int i = 0; i < kinds.Length; i++)
		{
			var k = kinds[i];
			Rectangle r = new Rectangle(x + (i % columns) * (tileW + 10), y + (i / columns) * (height + 8), tileW, height);
			string id = "fb:kind:" + i;
			bool active = _kind == k.Kind;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(id) && !active)
			{
				_kind = k.Kind;
				HubInput.Sound(12);
			}
			Color color = FeedbackReport.KindColor(k.Kind);
			float glow = HubInput.Anim(id, hover);
			float on = HubInput.Anim(id + ":on", active);
			Color back = Color.Lerp(Color.Lerp(Theme.Card, Theme.CardHover, glow), Color.Lerp(Theme.Card, color, 0.35f), on);
			HubKit.Box(sb, r, back * HubPrefs.Opacity);
			HubKit.Fill(sb, new Rectangle(r.X + 6, r.Y + 10, 3, r.Height - 20), color * (0.35f + 0.65f * on));
			int icon = 30 + (int)(4 * glow);
			HubIcon.Item(FeedbackReport.KindIcon(k.Kind)).Draw(sb, new Rectangle(r.X + 32 - icon / 2, r.Center.Y - icon / 2, icon, icon), Color.White);
			HubKit.Text(sb, k.Title, r.X + 56, r.Y + 11, active ? Color.White : Theme.Text, 0.86f, r.Width - 64);
			HubKit.Text(sb, k.Line, r.X + 56, r.Y + 34, active ? Color.Lerp(Theme.Muted, Color.White, 0.5f) : Theme.Muted, 0.66f, r.Width - 64);
		}
		int rows = (kinds.Length + columns - 1) / columns;
		return rows * height + (rows - 1) * 8;
	}

	private static int InfoBox(SpriteBatch sb, int x, int y, int width)
	{
		List<(string Name, string Value)> info = ReportBuilder.Info();
		string player = ReportBuilder.PlayerName();
		if (player.Length > 0)
		{
			info.Insert(0, ("Player", player));
		}
		const int labelW = 90;
		int height = 12 + info.Count * 18 + 8;
		Rectangle box = new Rectangle(x, y, width, height);
		HubKit.Box(sb, box, new Color(10, 12, 24) * 0.55f);
		for (int i = 0; i < info.Count; i++)
		{
			HubKit.Text(sb, info[i].Name, x + 14, y + 10 + i * 18, Theme.Muted, 0.66f, labelW - 8);
			HubKit.Text(sb, info[i].Value, x + 14 + labelW, y + 10 + i * 18, Theme.Text * 0.9f, 0.66f, width - labelW - 28);
		}
		return height;
	}

	private static int SendBar(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		int wait = FeedbackReports.WaitSeconds;
		bool sending = _justSent != null && _justSent.Status == ReportStatus.Sending;
		bool ready = !sending && wait == 0 && (_title.Trim().Length > 0 || _text.Trim().Length > 0);
		Color color = FeedbackReport.KindColor(_kind);
		string label = sending ? "Sending..." : (wait > 0 ? ("Wait " + FeedbackReports.Duration(wait)) : ("Send " + FeedbackReport.KindName(_kind).ToLower()));
		Rectangle button = new Rectangle(x + width - 210, y, 210, 40);
		if (HubKit.Button(sb, "fb:send", button, label, color * 0.75f, ready ? "Sends it to the developers" : null, ready, HubIcon.Item(I.PaperAirplaneA)) && ready)
		{
			Send();
		}
		if (HubKit.Button(sb, "fb:clear", new Rectangle(button.X - 108, y, 100, 40), "Clear", Theme.Slot, "Empties the form", _title.Length > 0 || _text.Length > 0))
		{
			ClearForm();
			HubInput.Sound(11);
		}

		// What happened to the last report: sending, sent, or why not.
		string status = null;
		Color statusColor = Theme.Muted;
		if (_justSent != null)
		{
			switch (_justSent.Status)
			{
			case ReportStatus.Sending:
				status = "Sending" + new string('.', (int)(Main.timeForVisualEffects / 20.0) % 4);
				break;
			case ReportStatus.Sent:
				status = "Sent! Thank you, the developers got it.";
				statusColor = Theme.On;
				break;
			case ReportStatus.Failed:
				status = "Not sent: " + _justSent.Error + " It is in My Reports to try again.";
				statusColor = Theme.Danger;
				break;
			}
		}
		if (_message != null && (DateTime.Now - _messageTime).TotalSeconds < 8)
		{
			status = _message;
			statusColor = _messageIsError ? Theme.Danger : Theme.On;
		}
		if (status != null)
		{
			string[] lines = HubKit.Wrap(status, button.X - 118 - x, 0.72f, 2);
			for (int i = 0; i < lines.Length; i++)
			{
				HubKit.Text(sb, lines[i], x + 4, y + (lines.Length == 1 ? 12 : 3) + i * 18, statusColor, 0.72f);
			}
		}
		else
		{
			HubKit.Text(sb, "Reports are private: only the IsaClient developers see them.", x + 4, y + 12, Theme.Muted * 0.85f, 0.68f, button.X - 118 - x);
		}
		y += 52;
		if (_justSent != null && _justSent.Status == ReportStatus.Sent)
		{
			y += Thanks(sb, x, y, width);
		}
		return y - start;
	}

	private static int Thanks(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 52);
		Color color = FeedbackReport.KindColor(_justSent.Kind);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, color, 0.25f) * HubPrefs.Opacity);
		float bob = (float)Math.Sin(Main.timeForVisualEffects * 0.06) * 2f;
		HubIcon.Item(I.HeartLantern).Draw(sb, new Rectangle(r.X + 12, r.Y + 10 + (int)bob, 32, 32), Color.White);
		HubKit.Text(sb, FeedbackReport.KindName(_justSent.Kind) + " #" + _justSent.Id + " sent", r.X + 54, r.Y + 8, Color.White, 0.8f, r.Width - 64);
		HubKit.Text(sb, "\"" + _justSent.Title + "\"", r.X + 54, r.Y + 29, Theme.Muted, 0.68f, r.Width - 64);
		return r.Height + 6;
	}

	private static void Send()
	{
		List<(string, string)> extra = new List<(string, string)>();
		if (_kind == ReportKind.Bug && _severity >= 0)
		{
			extra.Add(("Severity", Severities[_severity]));
		}
		if (_kind == ReportKind.Bug && _frequency >= 0)
		{
			extra.Add(("Happens", Frequencies[_frequency]));
		}
		if (_area >= 0)
		{
			extra.Add(("Area", Areas[_area]));
		}
		string contact = (_contact ?? "").Trim();
		if (contact != FeedbackReports.Contact)
		{
			FeedbackReports.Contact = contact;
			ClientSettings.Save();
		}
		int before = FeedbackReports.History.Count;
		string error = FeedbackReports.Submit(_kind, _title, _text, extra, _withInfo, _withErrors);
		if (error != null)
		{
			Say(error, true);
			HubInput.Sound(11);
			return;
		}
		_justSent = FeedbackReports.History.FirstOrDefault();
		_message = null;
		ClearForm();
		HubInput.Focus = null;
		HubInput.Sound(10);
	}

	private static void ClearForm()
	{
		_title = "";
		_text = "";
		_severity = -1;
		_frequency = -1;
		_area = -1;
		Main.clrInput();
	}

	private static void Say(string text, bool error)
	{
		_message = text;
		_messageIsError = error;
		_messageTime = DateTime.Now;
	}

	#endregion

	#region Crashes

	public static int DrawCrashes(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Hero(sb, x, y, width, HubIcon.Item(I.Dynamite), "Crash reports", "When IsaClient crashes, the error is sent to the developers so it gets fixed. Only the error, the client version, your Windows version, screen size, plugins and character name: nothing else.", FeedbackReport.KindColor(ReportKind.Crash));
		y += 14;

		y += Toggles(sb, x, y, width, new (string, string, string, bool)[]
		{
			("fb:auto", "Send crash reports automatically", "On: crashes are sent when they happen (or the next time the game starts). Off: they are only kept here for you to send.", FeedbackReports.AutoCrashReports)
		}, out int flipped) + 12;
		if (flipped == 0)
		{
			FeedbackReports.AutoCrashReports = !FeedbackReports.AutoCrashReports;
			ClientSettings.Save();
		}

		List<FeedbackReport> crashes = FeedbackReports.History.Where(r => r.Kind == ReportKind.Crash).ToList();
		y += Label(sb, "Crashes", x, y, (crashes.Count == 0) ? null : crashes.Count.ToString(), width);
		if (crashes.Count == 0)
		{
			y += Empty(sb, x, y, width, HubIcon.Item(I.FallenStar), "No crashes so far.", "If the game ever closes with an error, it shows up here.");
		}
		foreach (FeedbackReport crash in crashes)
		{
			y += ReportCard(sb, crash, x, y, width) + 8;
		}
		y += 6;
		if (HubKit.Button(sb, "fb:folder", new Rectangle(x, y, 200, 32), "Open reports folder", Theme.Slot, "IsaClient/Feedback: the history of your reports", true, HubIcon.Item(I.Chest)))
		{
			try
			{
				System.IO.Directory.CreateDirectory(System.IO.Path.Combine(ClientSettings.IsaClientFolder, "Feedback"));
				Utils.OpenFolder(System.IO.Path.Combine(ClientSettings.IsaClientFolder, "Feedback"));
			}
			catch (Exception ex)
			{
				ClientLog.LogOnce(ex);
			}
		}
		y += 40;
		return y - start;
	}

	#endregion

	#region My reports

	public static int DrawHistory(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		List<FeedbackReport> all = FeedbackReports.History;
		(string Name, ReportKind? Kind)[] filters = { ("All", null), ("Bugs", ReportKind.Bug), ("Suggestions", ReportKind.Suggestion), ("Feedback", ReportKind.Feedback), ("Crashes", ReportKind.Crash) };
		HubIcon.Item(I.Book).Draw(sb, new Rectangle(x + 2, y + 2, 22, 22), Color.White);
		HubKit.Text(sb, "My reports", x + 30, y + 4, Theme.Accent, 0.92f);
		int sent = all.Count(r => r.Status == ReportStatus.Sent);
		HubKit.Text(sb, sent + " sent" + (all.Count > sent ? ", " + (all.Count - sent) + " not sent" : ""), x + 30, y + 28, Theme.Muted, 0.7f);
		if (HubKit.Button(sb, "fb:clearall", new Rectangle(x + width - 110, y + 4, 110, 32), "Clear all", Theme.Slot, "Removes the list here (what was sent stays sent)", all.Count > 0))
		{
			FeedbackReports.ClearHistory();
			HubInput.Sound(11);
		}
		y += 54;

		int cx = x;
		for (int i = 0; i < filters.Length; i++)
		{
			int count = (filters[i].Kind == null) ? all.Count : all.Count(r => r.Kind == filters[i].Kind);
			string label = filters[i].Name + "  " + count;
			int w = (int)HubKit.Measure(label, 0.7f) + 22;
			Rectangle r = new Rectangle(cx, y, w, ChipHeight);
			if (Chip(sb, "fb:filter:" + i, r, label, _filter == i, (filters[i].Kind == null) ? Theme.Accent : FeedbackReport.KindColor(filters[i].Kind.Value)))
			{
				_filter = i;
			}
			cx += w + 6;
		}
		y += ChipHeight + 14;

		ReportKind? kind = filters[Utils.Clamp(_filter, 0, filters.Length - 1)].Kind;
		List<FeedbackReport> shown = all.Where(r => kind == null || r.Kind == kind).ToList();
		if (shown.Count == 0)
		{
			y += Empty(sb, x, y, width, HubIcon.Item(I.PaperAirplaneA), "Nothing here yet.", "Reports you send show up here with their status.");
		}
		foreach (FeedbackReport report in shown)
		{
			y += ReportCard(sb, report, x, y, width) + 8;
		}
		return y - start;
	}

	/// <summary>A report: kind icon, title, when, status chip, the text, and Send again / Copy / Forget.</summary>
	private static int ReportCard(SpriteBatch sb, FeedbackReport report, int x, int y, int width)
	{
		Color color = FeedbackReport.KindColor(report.Kind);
		string[] lines = HubKit.Wrap((report.Text ?? "").Replace("\r", ""), width - 70, 0.68f, 2);
		string error = (report.Status == ReportStatus.Failed || report.Status == ReportStatus.Saved) ? report.Error : null;
		int height = 40 + lines.Length * 17 + (error != null ? 18 : 0) + 38;
		Rectangle r = new Rectangle(x, y, width, height);
		if (!HubInput.Visible(r))
		{
			return height;
		}
		string id = "fb:card:" + report.Id;
		bool hover = HubInput.In(r);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(id, hover)) * HubPrefs.Opacity);
		HubKit.Fill(sb, new Rectangle(r.X + 6, r.Y + 10, 3, r.Height - 20), color * 0.8f);
		HubIcon.Item(FeedbackReport.KindIcon(report.Kind)).Draw(sb, new Rectangle(r.X + 18, r.Y + 12, 26, 26), Color.White);

		(string Text, Color Color) status = report.Status switch
		{
			ReportStatus.Sent => ("Sent", Theme.On),
			ReportStatus.Sending => ("Sending", new Color(255, 210, 90)),
			ReportStatus.Failed => ("Not sent", Theme.Danger),
			_ => ("Saved here", Theme.Muted)
		};
		Rectangle chip = HubKit.Chip(sb, status.Text, r.Right - 12, r.Y + 12, status.Color * 0.35f, Color.Lerp(status.Color, Color.White, 0.4f), 0.66f, alignRight: true);
		string when = Ago(report.Time);
		float whenW = HubKit.Measure(when, 0.64f);
		HubKit.Text(sb, when, chip.X - 10 - whenW, r.Y + 15, Theme.Muted, 0.64f);
		HubKit.Text(sb, FeedbackReport.KindName(report.Kind) + ": " + report.Title, r.X + 54, r.Y + 13, Theme.Text, 0.78f, chip.X - whenW - 24 - (r.X + 54));
		int ty = r.Y + 40;
		foreach (string line in lines)
		{
			HubKit.Text(sb, line, r.X + 54, ty, Theme.Muted, 0.68f);
			ty += 17;
		}
		if (error != null)
		{
			HubKit.Text(sb, error, r.X + 54, ty, (report.Status == ReportStatus.Failed) ? Theme.Danger : Theme.Muted, 0.64f, r.Width - 66);
		}

		int bx = r.Right - 12;
		int by = r.Bottom - 36;
		bx -= 84;
		if (HubKit.Button(sb, id + ":forget", new Rectangle(bx, by, 84, 26), "Forget", Theme.Slot, "Removes it from this list", report.Status != ReportStatus.Sending))
		{
			FeedbackReports.Forget(report);
			HubInput.Sound(11);
		}
		if (report.Attachment != null || report.Text != null)
		{
			bx -= 90;
			if (HubKit.Button(sb, id + ":copy", new Rectangle(bx, by, 84, 26), "Copy", Theme.Slot, (report.Kind == ReportKind.Crash) ? "Copies the whole crash log" : "Copies the text"))
			{
				Platform.Get<IClipboard>().Value = (report.Kind == ReportKind.Crash && report.Attachment != null) ? report.Attachment : (report.Title + "\n\n" + report.Text);
				HubInput.Sound(12);
			}
		}
		if (report.Status == ReportStatus.Failed || report.Status == ReportStatus.Saved)
		{
			bx -= 110;
			if (HubKit.Button(sb, id + ":retry", new Rectangle(bx, by, 104, 26), (report.Status == ReportStatus.Saved) ? "Send" : "Send again", color * 0.7f, "Sends it to the developers"))
			{
				string why = FeedbackReports.Retry(report);
				if (why != null)
				{
					ClientChat.Error(why);
				}
				HubInput.Sound(12);
			}
		}
		HubKit.Text(sb, "#" + report.Id, r.X + 54, by + 5, Theme.Muted * 0.6f, 0.62f);
		return height;
	}

	#endregion

	#region Pieces

	/// <summary>The banner at the top of a page: big icon, title in the page color and a short explanation.</summary>
	private static int Hero(SpriteBatch sb, int x, int y, int width, HubIcon icon, string title, string text, Color color)
	{
		string[] lines = HubKit.Wrap(text, width - 92, 0.72f, 3);
		int height = Math.Max(72, 42 + lines.Length * 18 + 10);
		Rectangle r = new Rectangle(x, y, width, height);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, color, 0.18f) * HubPrefs.Opacity);
		float bob = (float)Math.Sin(Main.timeForVisualEffects * 0.045) * 2f;
		icon.Draw(sb, new Rectangle(r.X + 16, r.Y + (height - 44) / 2 + (int)bob, 44, 44), Color.White);
		HubKit.Text(sb, title, r.X + 76, r.Y + 12, Color.Lerp(color, Color.White, 0.35f), 0.95f, width - 92);
		for (int i = 0; i < lines.Length; i++)
		{
			HubKit.Text(sb, lines[i], r.X + 76, r.Y + 40 + i * 18, Theme.Text * 0.85f, 0.72f);
		}
		return height;
	}

	private static int Label(SpriteBatch sb, string text, int x, int y, string right = null, int width = 0)
	{
		HubKit.Text(sb, text, x + 2, y, Theme.Text, 0.8f);
		if (right != null)
		{
			float w = HubKit.Measure(right, 0.64f);
			HubKit.Text(sb, right, x + width - w - 4, y + 3, Theme.Muted * 0.8f, 0.64f);
		}
		return 24;
	}

	private static int Empty(SpriteBatch sb, int x, int y, int width, HubIcon icon, string title, string text)
	{
		Rectangle r = new Rectangle(x, y, width, 62);
		HubKit.Box(sb, r, new Color(10, 12, 24) * 0.45f);
		icon.Draw(sb, new Rectangle(r.X + 16, r.Y + 15, 32, 32), Color.White * 0.8f);
		HubKit.Text(sb, title, r.X + 60, r.Y + 12, Theme.Text, 0.78f);
		HubKit.Text(sb, text, r.X + 60, r.Y + 34, Theme.Muted, 0.66f, r.Width - 70);
		return r.Height + 8;
	}

	private static bool Chip(SpriteBatch sb, string id, Rectangle r, string label, bool active, Color color)
	{
		bool hover = HubInput.In(r);
		bool clicked = hover && HubInput.Hover(id);
		if (clicked)
		{
			HubInput.Sound(12);
		}
		float glow = HubInput.Anim(id, hover || active);
		Color back = active ? Color.Lerp(Theme.Slot, color, 0.45f) : Color.Lerp(Theme.Card, Theme.CardHover, glow);
		HubKit.Box(sb, r, back * HubPrefs.Opacity);
		HubKit.Text(sb, label, r.X + 10, r.Y + 6, active ? Color.White : Theme.Text, 0.7f);
		return clicked;
	}

	/// <summary>A label and chips that wrap; clicking the picked one again unpicks it.</summary>
	private static int ChipRow(SpriteBatch sb, string id, string label, int x, int y, int width, string[] options, ref int picked)
	{
		const int labelW = 140;
		HubKit.Text(sb, label, x + 2, y + 6, Theme.Muted, 0.72f, labelW - 8);
		int left = x + labelW;
		int cx = left;
		int cy = y;
		Color color = FeedbackReport.KindColor(_kind);
		for (int i = 0; i < options.Length; i++)
		{
			int w = (int)HubKit.Measure(options[i], 0.7f) + 20;
			if (cx > left && cx + w > x + width)
			{
				cx = left;
				cy += ChipHeight + 6;
			}
			Rectangle r = new Rectangle(cx, cy, w, ChipHeight);
			cx += w + 6;
			if (Chip(sb, id + ":" + i, r, options[i], picked == i, color))
			{
				picked = (picked == i) ? -1 : i;
			}
		}
		return cy + ChipHeight + 10 - y;
	}

	/// <summary>Wide tiles with a switch; <paramref name="flipped"/> is the index clicked this frame (-1 = none).</summary>
	private static int Toggles(SpriteBatch sb, int x, int y, int width, (string Id, string Title, string Tip, bool On)[] toggles, out int flipped)
	{
		flipped = -1;
		int columns = (width >= 560 && toggles.Length > 1) ? 2 : 1;
		int tileW = (width - (columns - 1) * 8) / columns;
		const int height = 40;
		for (int i = 0; i < toggles.Length; i++)
		{
			var t = toggles[i];
			Rectangle r = new Rectangle(x + (i % columns) * (tileW + 8), y + (i / columns) * (height + 8), tileW, height);
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(t.Id, t.Tip))
			{
				flipped = i;
				HubInput.Sound(12);
			}
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(t.Id, hover)) * HubPrefs.Opacity);
			HubKit.Text(sb, t.Title, r.X + 12, r.Y + 11, t.On ? Theme.Text : Theme.Muted, 0.76f, r.Width - 76);
			HubKit.Switch(sb, t.Id + ":sw", new Rectangle(r.Right - 56, r.Y + 9, 44, 22), t.On, hover);
		}
		int rows = (toggles.Length + columns - 1) / columns;
		return rows * height + (rows - 1) * 8;
	}

	/// <summary>A text box of several lines: Enter makes a new line, it grows with the text (and shows the end while typing).</summary>
	private static int TextArea(SpriteBatch sb, string id, int x, int y, int width, ref string value, string placeholder, int maxLength)
	{
		bool focused = HubInput.Focus == id;
		List<string> lines = WrapHard(value ?? "", width - 28, BodyScale);
		int visible = Utils.Clamp(lines.Count, 5, 12);
		Rectangle r = new Rectangle(x, y, width, 16 + visible * LineHeight);
		if (HubInput.Visible(r))
		{
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover("field:" + id) && !focused)
			{
				HubInput.SetFocus(id);
				focused = true;
			}
			float glow = HubInput.Anim("field:" + id, focused || hover);
			HubKit.Box(sb, r, Color.Lerp(Theme.Sidebar, Theme.CardHover, glow * 0.6f) * 0.98f);
			if (focused)
			{
				HubKit.Fill(sb, new Rectangle(r.X + 8, r.Bottom - 4, r.Width - 16, 2), Theme.Accent * 0.8f);
			}
		}
		if (focused)
		{
			value = HubInput.TypeInto(value, maxLength);
			if (Main.inputTextEnter && value.Length < maxLength)
			{
				value += "\n";
				Main.inputTextEnter = false;
			}
			focused = HubInput.Focus == id;
			lines = WrapHard(value, width - 28, BodyScale);
		}
		if (!HubInput.Visible(r))
		{
			return r.Height;
		}
		if (string.IsNullOrEmpty(value))
		{
			string[] hint = HubKit.Wrap(placeholder, width - 28, BodyScale, visible);
			for (int i = 0; i < hint.Length; i++)
			{
				HubKit.Text(sb, hint[i], r.X + 12, r.Y + 10 + i * LineHeight, Theme.Muted * 0.85f, BodyScale);
			}
			if (focused && Main.GameUpdateCount / 30 % 2 == 0)
			{
				HubKit.Text(sb, "|", r.X + 10, r.Y + 10, Color.White, BodyScale);
			}
			return r.Height;
		}
		int first = (lines.Count > visible) ? (focused ? lines.Count - visible : 0) : 0;
		for (int i = 0; i < visible && first + i < lines.Count; i++)
		{
			string line = lines[first + i];
			if (focused && first + i == lines.Count - 1 && Main.GameUpdateCount / 30 % 2 == 0)
			{
				line += "|";
			}
			HubKit.Text(sb, line, r.X + 12, r.Y + 10 + i * LineHeight, Color.White, BodyScale);
		}
		if (lines.Count > visible)
		{
			string more = focused ? ("^ " + first + " more lines") : ("+ " + (lines.Count - visible) + " more lines");
			float w = HubKit.Measure(more, 0.6f);
			HubKit.Text(sb, more, r.Right - w - 12, r.Bottom - 20, Theme.Muted * 0.8f, 0.6f);
		}
		return r.Height;
	}

	/// <summary>Word wrap that also cuts words longer than a line (links, paths).</summary>
	private static List<string> WrapHard(string text, float maxWidth, float scale)
	{
		List<string> lines = new List<string>();
		foreach (string paragraph in text.Replace("\r", "").Split('\n'))
		{
			string line = "";
			foreach (string word in paragraph.Split(' '))
			{
				string next = (line.Length == 0) ? word : (line + " " + word);
				if (HubKit.Measure(next, scale) <= maxWidth)
				{
					line = next;
					continue;
				}
				if (line.Length > 0)
				{
					lines.Add(line);
				}
				line = word;
				while (line.Length > 1 && HubKit.Measure(line, scale) > maxWidth)
				{
					int cut = line.Length - 1;
					while (cut > 1 && HubKit.Measure(line.Substring(0, cut), scale) > maxWidth)
					{
						cut--;
					}
					lines.Add(line.Substring(0, cut));
					line = line.Substring(cut);
				}
			}
			lines.Add(line);
		}
		return lines;
	}

	private static string Ago(DateTime time)
	{
		TimeSpan span = DateTime.Now - time;
		if (span.TotalMinutes < 1)
		{
			return "just now";
		}
		if (span.TotalHours < 1)
		{
			return (int)span.TotalMinutes + " min ago";
		}
		if (span.TotalDays < 1)
		{
			return (int)span.TotalHours + " h ago";
		}
		return (span.TotalDays < 7) ? ((int)span.TotalDays + " d ago") : time.ToString("MMM d");
	}

	#endregion
}
