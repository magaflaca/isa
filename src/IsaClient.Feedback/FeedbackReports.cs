using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using IsaClient.Core;
using Newtonsoft.Json;

namespace IsaClient.Feedback;

/// <summary>
/// Bug reports, suggestions and crash reports for the developers. Reports go out in the background and are kept in
/// IsaClient/Feedback/Reports.json (status, and the exact message to send it again). Crashes are saved to disk first,
/// then sent while the game closes; what could not go out is sent on the next start.
/// </summary>
public static class FeedbackReports
{
	[Setting("AutoCrashReports")]
	public static bool AutoCrashReports = true;

	/// <summary>The Discord name the user gave so the developers can ask them about a report.</summary>
	[Setting("FeedbackContact")]
	public static string Contact = "";

	/// <summary>A random id of this install: tells reports of the same person apart without saying who it is.</summary>
	[Setting("FeedbackInstallId")]
	public static string InstallId = "";

	public const int TitleLimit = 100;

	public const int TextLimit = 2500;

	public const int ContactLimit = 40;

	/// <summary>Time between two reports sent by hand.</summary>
	private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(45);

	private const int PerHour = 8;

	private const int CrashesPerDay = 6;

	private static readonly object _lock = new object();

	private static List<FeedbackReport> _history;

	private static readonly HashSet<string> _crashesThisRun = new HashSet<string>();

	private static DateTime _lastManual = DateTime.MinValue;

	private static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "Feedback");

	private static string HistoryPath => Path.Combine(Folder, "Reports.json");

	public static string InstallTag
	{
		get
		{
			if (string.IsNullOrEmpty(InstallId))
			{
				InstallId = Guid.NewGuid().ToString("N").Substring(0, 10);
				try
				{
					ClientSettings.Save();
				}
				catch
				{
				}
			}
			return InstallId;
		}
	}

	/// <summary>Newest first.</summary>
	public static List<FeedbackReport> History
	{
		get
		{
			lock (_lock)
			{
				EnsureLoaded();
				return _history.OrderByDescending(r => r.Time).ToList();
			}
		}
	}

	/// <summary>Seconds until another report can be sent by hand (0 = now).</summary>
	public static int WaitSeconds
	{
		get
		{
			double cooldown = (_lastManual + Cooldown - DateTime.Now).TotalSeconds;
			if (cooldown > 0)
			{
				return (int)Math.Ceiling(cooldown);
			}
			lock (_lock)
			{
				EnsureLoaded();
				List<DateTime> lastHour = _history.Where(r => r.Kind != ReportKind.Crash && r.Time > DateTime.Now.AddHours(-1)).Select(r => r.Time).OrderBy(t => t).ToList();
				return (lastHour.Count < PerHour) ? 0 : Math.Max(1, (int)Math.Ceiling((lastHour[0].AddHours(1) - DateTime.Now).TotalSeconds));
			}
		}
	}

	/// <summary>Called once the client settings are loaded: watches other threads for crashes and sends crashes left from last time.</summary>
	public static void Initialize()
	{
		AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
		{
			if (args.ExceptionObject is Exception ex)
			{
				OnCrash(ex, "Thread " + (Thread.CurrentThread.Name ?? Thread.CurrentThread.ManagedThreadId.ToString()));
			}
		};
		ThreadPool.QueueUserWorkItem(delegate
		{
			List<FeedbackReport> waiting;
			lock (_lock)
			{
				EnsureLoaded();
				foreach (FeedbackReport report in _history.Where(r => r.Status == ReportStatus.Sending))
				{
					report.Status = ReportStatus.Failed;
					report.Error = "The game closed while it was being sent.";
				}
				waiting = AutoCrashReports ? _history.Where(r => r.Kind == ReportKind.Crash && r.Status == ReportStatus.Failed && r.Time > DateTime.Now.AddDays(-7)).Take(3).ToList() : new List<FeedbackReport>();
				Save();
			}
			foreach (FeedbackReport report in waiting)
			{
				SendNow(report, 15000);
			}
		});
	}

	#region Reports by hand

	/// <summary>Sends a bug, suggestion or feedback in the background. Returns null when it went out, else why not.</summary>
	public static string Submit(ReportKind kind, string title, string text, IEnumerable<(string Name, string Value)> extra, bool withInfo, bool withErrors)
	{
		title = (title ?? "").Trim();
		text = (text ?? "").Trim();
		if (title.Length == 0 && text.Length == 0)
		{
			return "Write what happened first.";
		}
		int wait = WaitSeconds;
		if (wait > 0)
		{
			return "Wait " + Duration(wait) + " before sending another report.";
		}
		if (title.Length == 0)
		{
			title = FirstWords(text);
		}
		FeedbackReport report = new FeedbackReport
		{
			Id = NewId(),
			Kind = kind,
			Title = ReportBuilder.Cut(title, TitleLimit),
			Text = ReportBuilder.Cut(text, TextLimit),
			Time = DateTime.Now,
			Status = ReportStatus.Sending
		};
		List<(string, string)> fields = extra?.ToList() ?? new List<(string, string)>();
		string contact = (Contact ?? "").Trim();
		if (contact.Length > 0)
		{
			fields.Add(("Contact", ReportBuilder.Cut(contact, ContactLimit)));
		}
		string full = null;
		string errors = withErrors ? RecentErrors(out full) : null;
		if (full != null)
		{
			report.Attachment = full;
			report.AttachmentName = "errors.txt";
		}
		report.Payload = ReportBuilder.Payload(report, report.Text.Length > 0 ? report.Text : "*(no details)*", fields, withInfo, errors);
		_lastManual = DateTime.Now;
		Add(report);
		ThreadPool.QueueUserWorkItem(delegate
		{
			SendNow(report, 20000);
		});
		return null;
	}

	/// <summary>Sends a failed or saved report again (by hand: same cooldown as new ones, except crashes).</summary>
	public static string Retry(FeedbackReport report)
	{
		if (report.Status == ReportStatus.Sending || report.Status == ReportStatus.Sent)
		{
			return null;
		}
		if (report.Kind != ReportKind.Crash && (_lastManual + Cooldown) > DateTime.Now)
		{
			return "Wait " + Duration(WaitSeconds) + " before sending again.";
		}
		_lastManual = DateTime.Now;
		report.Status = ReportStatus.Sending;
		report.Error = null;
		ThreadPool.QueueUserWorkItem(delegate
		{
			SendNow(report, 20000);
		});
		return null;
	}

	public static void Forget(FeedbackReport report)
	{
		lock (_lock)
		{
			EnsureLoaded();
			_history.Remove(report);
			Save();
		}
	}

	public static void ClearHistory()
	{
		lock (_lock)
		{
			EnsureLoaded();
			_history.RemoveAll(r => r.Status != ReportStatus.Sending);
			Save();
		}
	}

	#endregion

	#region Crashes

	/// <summary>
	/// The game is closing because of <paramref name="ex"/>: saves a crash report and, when automatic reports are on,
	/// sends it now (waiting a few seconds at most). Never throws.
	/// </summary>
	public static void OnCrash(Exception ex, string where)
	{
		try
		{
			string signature = Signature(ex);
			lock (_lock)
			{
				if (!_crashesThisRun.Add(signature))
				{
					return;
				}
				EnsureLoaded();
			}
			FeedbackReport report = new FeedbackReport
			{
				Id = NewId(),
				Kind = ReportKind.Crash,
				Title = ex.GetType().Name,
				Text = ex.Message,
				Time = DateTime.Now,
				Signature = signature,
				Status = ReportStatus.Saved
			};
			string errors = RecentErrors(out string earlier);
			StringBuilder log = new StringBuilder();
			log.AppendLine(ClientInfo.DisplayName + " crash, " + report.Time.ToString("yyyy-MM-dd HH:mm:ss") + " (" + where + ")");
			foreach ((string name, string value) in ReportBuilder.Info())
			{
				log.AppendLine(name + ": " + value);
			}
			log.AppendLine().AppendLine(ex.ToString());
			if (earlier != null)
			{
				log.AppendLine().AppendLine("Errors before the crash:").AppendLine(earlier);
			}
			report.Attachment = log.ToString();
			report.AttachmentName = "crash-" + report.Id + ".txt";
			string details = "**" + ReportBuilder.Cut(ex.Message, 300) + "**\n```\n" + ReportBuilder.Cut(ShortTrace(ex), 2200) + "\n```";
			report.Payload = ReportBuilder.Payload(report, details, new[] { ("Thread", where) }, withInfo: true, errors);
			bool send;
			lock (_lock)
			{
				bool repeat = _history.Any(r => r.Kind == ReportKind.Crash && r.Signature == signature && r.Status == ReportStatus.Sent && r.Time > DateTime.Now.AddDays(-1));
				int today = _history.Count(r => r.Kind == ReportKind.Crash && r.Status == ReportStatus.Sent && r.Time > DateTime.Now.AddDays(-1));
				send = AutoCrashReports && !repeat && today < CrashesPerDay;
				if (repeat)
				{
					report.Error = "The same crash was already reported today.";
				}
				// On disk before trying: if the game dies while sending, the next start sends it.
				report.Status = send ? ReportStatus.Failed : ReportStatus.Saved;
				_history.Add(report);
				Trim();
				Save();
			}
			if (send)
			{
				SendNow(report, 7000);
			}
		}
		catch
		{
		}
	}

	/// <summary>"A crash report was sent..." for the crash window, or null when nothing was sent.</summary>
	public static string CrashNote(Exception ex)
	{
		try
		{
			string signature = Signature(ex);
			FeedbackReport report;
			lock (_lock)
			{
				report = _history?.LastOrDefault(r => r.Signature == signature);
			}
			if (report == null)
			{
				return null;
			}
			return report.Status switch
			{
				ReportStatus.Sent => "A crash report was sent to the IsaClient developers. Thanks!",
				ReportStatus.Failed => "The crash report could not be sent now; IsaClient will send it next time it starts.",
				_ => AutoCrashReports ? null : "Crash saved. You can send it from the panel: Report a Bug > Crashes."
			};
		}
		catch
		{
			return null;
		}
	}

	private static string Signature(Exception ex)
	{
		string frames = string.Join("|", (ex.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim()));
		return ex.GetType().FullName + "|" + frames;
	}

	/// <summary>The stack trace without the file paths, inner exceptions included.</summary>
	private static string ShortTrace(Exception ex)
	{
		StringBuilder text = new StringBuilder();
		for (Exception e = ex; e != null; e = e.InnerException)
		{
			if (e != ex)
			{
				text.AppendLine("--- " + e.GetType().Name + ": " + e.Message);
			}
			foreach (string line in (e.StackTrace ?? "").Split('\n').Take(18))
			{
				string frame = line.Trim();
				int file = frame.IndexOf(" in ", StringComparison.Ordinal);
				text.AppendLine((file > 0) ? frame.Substring(0, file) : frame);
			}
		}
		return text.ToString().TrimEnd();
	}

	#endregion

	#region Sending and history

	private static void SendNow(FeedbackReport report, int timeoutMs)
	{
		report.Status = ReportStatus.Sending;
		string error = ReportSender.Send(report, timeoutMs);
		lock (_lock)
		{
			report.Status = (error == null) ? ReportStatus.Sent : ReportStatus.Failed;
			report.Error = error;
			Save();
		}
	}

	private static void Add(FeedbackReport report)
	{
		lock (_lock)
		{
			EnsureLoaded();
			_history.Add(report);
			Trim();
			Save();
		}
	}

	private static void Trim()
	{
		while (_history.Count > 60)
		{
			_history.Remove(_history.OrderBy(r => r.Time).First());
		}
	}

	private static void EnsureLoaded()
	{
		if (_history != null)
		{
			return;
		}
		try
		{
			_history = File.Exists(HistoryPath) ? JsonConvert.DeserializeObject<List<FeedbackReport>>(File.ReadAllText(HistoryPath)) : null;
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
		_history ??= new List<FeedbackReport>();
	}

	private static void Save()
	{
		try
		{
			Directory.CreateDirectory(Folder);
			string temp = HistoryPath + ".tmp";
			File.WriteAllText(temp, JsonConvert.SerializeObject(_history, Formatting.Indented));
			if (File.Exists(HistoryPath))
			{
				File.Delete(HistoryPath);
			}
			File.Move(temp, HistoryPath);
		}
		catch
		{
		}
	}

	/// <summary>The headlines of this session's errors for the embed; <paramref name="full"/> gets them whole (null when none).</summary>
	private static string RecentErrors(out string full)
	{
		List<(DateTime Time, string Text)> recent = ClientLog.Recent;
		full = null;
		if (recent.Count == 0)
		{
			return null;
		}
		full = string.Join("\n\n", recent.Select(e => e.Time.ToString("HH:mm:ss") + "\n" + e.Text));
		return string.Join("\n", recent.Skip(Math.Max(0, recent.Count - 5)).Select(e => e.Time.ToString("HH:mm:ss") + " " + ReportBuilder.Cut(e.Text.Split('\n')[0].Trim(), 170)));
	}

	private static string NewId()
	{
		return Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
	}

	private static string FirstWords(string text)
	{
		string line = text.Split('\n')[0].Trim();
		return (line.Length <= 60) ? line : (line.Substring(0, 57).TrimEnd() + "...");
	}

	public static string Duration(int seconds)
	{
		return (seconds < 60) ? (seconds + "s") : ((seconds / 60) + "m " + (seconds % 60) + "s");
	}

	#endregion
}
