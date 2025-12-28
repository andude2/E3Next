using MonoCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static MonoCore.E3ImGUI;
using E3Core.Classes;
using E3Core.Processors;
using E3Core.Server;
using E3Core.Utility;

namespace E3Core.UI.Windows.MemStats
{
	public static class MemoryStatsWindow
	{
		private static bool _windowInitialized = false;
		private static bool _imguiContextReady = false;
		private static Int64 _lastUpdate = 0;
		private static Int64 _lastUpdateInterval = 1000;
		private static List<MemoryStats> _memoryStats = new List<MemoryStats>();

		private static string _WindowName = "E3 Memory Stats";
		private const string _TopicPopupWindowName = "E3 Topic Subscriptions";
		// Severity legend doubles as the palette we reuse for each EQ commit range.
		private static readonly (double MinGb, double MaxGb, float R, float G, float B, string Label)[] _eqCommitSeverityBands = new[]
		{
			(0.0, 0.8, 0.6f, 0.9f, 0.6f, "<0.8 GB = Plenty of headroom"),
			(0.8, 1.2, 0.25f, 0.85f, 0.25f, "0.8-1.2 GB = Rock solid"),
			(1.2, 1.3, 0.95f, 0.85f, 0.35f, "1.2-1.3 GB = Mostly stable"),
			(1.3, 1.4, 1.0f, 0.7f, 0.2f, "1.3-1.4 GB = Possible crash"),
            // Treat anything past 1.4 GB up to the 1.6 GB wall as "soon to crash" (matches the user's warning).
            (1.4, 1.6, 1.0f, 0.35f, 0.2f, "1.4-1.5 GB = Soon to crash (large zones)"),
			(1.6, double.MaxValue, 1.0f, 0.05f, 0.05f, "1.6+ GB = Crash very likely")
		};


		[SubSystemInit]
		public static void Init()
		{
			if (Core._MQ2MonoVersion < 0.36m) return;
			E3ImGUI.RegisterWindow(_WindowName, RenderWindow);

			EventProcessor.RegisterCommand("/e3memstats", (x) =>
			{
				MemoryStatsWindow.ToggleWindow();
			}, "toggle memory stats window");
		}
		public static void ToggleWindow()
		{
			try
			{
				// Check if ImGUI is available before attempting to use it
				if (!E3ImGUI.IsImGuiAvailable())
				{
					E3.Bots.Broadcast("\\arImGUI is not available. Please ensure MQ2Mono version is 0.36 or higher and ImGUI is loaded.");
					return;
				}

				if (!_windowInitialized)
				{
					_windowInitialized = true;
					imgui_Begin_OpenFlagSet(_WindowName, true);
				}
				else
				{
					bool open = imgui_Begin_OpenFlagGet(_WindowName);
					bool newState = !open;
					imgui_Begin_OpenFlagSet(_WindowName, newState);
				}
				_imguiContextReady = true;
			}
			catch (Exception ex)
			{
				E3.Log.Write($"Memory Stats Window error: {ex.Message}", Logging.LogLevels.Error);
				E3.Bots.Broadcast($"\\arMemory Stats Window error: {ex.Message}");
				_imguiContextReady = false;
			}
		}

		private static void CheckRefresh()
		{
			if (!e3util.ShouldCheck(ref _lastUpdate, _lastUpdateInterval)) return;
			_memoryStats.Clear();
			//get the connected bots.
			List<string> users = E3.Bots.BotsConnected().ToList(); //make a copy as this returns a direct copy of cache
			users.Sort();
			foreach (var user in users)
			{
				Double csharpMemory = 0;
				Double eqPageMemory = 0;

				string startTime = E3.Bots.Query(user, "${Me.Memory_CSharpStartTime}");
				var memoryStat = new MemoryStats(user, csharpMemory, eqPageMemory);
				memoryStat.CommandQueueDepth = ParseInt(E3.Bots.Query(user, "${Me.Queue_CommandQueue}"));
				memoryStat.CommandQueueDrops = ParseInt(E3.Bots.Query(user, "${Me.QueueDrops_CommandQueue}"));
				memoryStat.ImguiQueueDepth = ParseInt(E3.Bots.Query(user, "${Me.Queue_IMGUICommands}"));
				memoryStat.ImguiQueueDrops = ParseInt(E3.Bots.Query(user, "${Me.QueueDrops_IMGUICommands}"));
				memoryStat.RouterRequestDepth = ParseInt(E3.Bots.Query(user, "${Me.Queue_TloRequests}"));
				memoryStat.RouterResponseDepth = ParseInt(E3.Bots.Query(user, "${Me.Queue_TloResponses}"));
				memoryStat.RouterDropCount = ParseInt(E3.Bots.Query(user, "${Me.QueueDrops_TloRequests}"));
				memoryStat.PubQueueDepth = ParseInt(E3.Bots.Query(user, "${Me.Queue_PubTopics}"));
				memoryStat.PubQueueDrops = ParseInt(E3.Bots.Query(user, "${Me.QueueDrops_PubTopics}"));

				E3.Bots.GetMemoryUsage(user, out csharpMemory, out eqPageMemory);
				memoryStat.CSharpMemoryMB = csharpMemory;
				memoryStat.EQCommitSizeMB = eqPageMemory;

				if (DateTime.TryParse(startTime, out var result))
				{
					memoryStat.TimeRunning = (System.DateTime.Now - result).TotalHours.ToString("N2");

				}



				_memoryStats.Add(memoryStat);
			}
		}
		private static void RenderWindow()
		{
			if (!_imguiContextReady) return;
			if (!imgui_Begin_OpenFlagGet(_WindowName))
			{
				// Close the topic popup if the parent window is hidden to avoid orphaned popups
				imgui_Begin_OpenFlagSet(_TopicPopupWindowName, false);
				return;
			}
			CheckRefresh();
			imgui_SetNextWindowSizeWithCond(600, 400, (int)ImGuiCond.FirstUseEver);
			E3ImGUI.PushCurrentTheme();
			try
			{
				using (var window = ImGUIWindow.Aquire())
				{
					if (!window.Begin(_WindowName, (int)ImGuiWindowFlags.ImGuiWindowFlags_NoCollapse))
						return;

					// Header with refresh button
					imgui_Text("E3 Memory Statistics by Rekka/Linamas");
					imgui_SameLine();
					if (imgui_Button("Topic Subscriptions"))
					{
						imgui_Begin_OpenFlagSet(_TopicPopupWindowName, true);
					}
					imgui_Separator();

					// Memory Stats Table
					using (var table = ImGUITable.Aquire())
					{
						int tableFlags = (int)(ImGuiTableFlags.ImGuiTableFlags_RowBg |
											  ImGuiTableFlags.ImGuiTableFlags_BordersOuter |
											  ImGuiTableFlags.ImGuiTableFlags_BordersInner |
											  ImGuiTableFlags.ImGuiTableFlags_ScrollY| ImGuiTableFlags.ImGuiTableFlags_Resizable);

						const float summaryLegendHeight = 60f; // Enough room for summary metrics plus multi-line legend
						float tableHeight = Math.Max(150f, imgui_GetContentRegionAvailY() - summaryLegendHeight);

					if (table.BeginTable("MemoryStatsTable", 4, tableFlags, 0f, tableHeight))
						{
							imgui_TableSetupColumn("Character", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthStretch, 150);
							imgui_TableSetupColumn("C# Memory (MB)", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthFixed, 120);
							imgui_TableSetupColumn("EQ Commit (MB)", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthFixed, 120);
							imgui_TableSetupColumn("Hours Running", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthStretch, 110);
							imgui_TableHeadersRow();

							List<MemoryStats> currentStats = _memoryStats;
						
							foreach (var stats in currentStats)
							{
								imgui_TableNextRow();

								imgui_TableNextColumn();
								imgui_Text(stats.CharacterName);

								imgui_TableNextColumn();
								imgui_Text(stats.CSharpMemoryMB.ToString("N2"));

								imgui_TableNextColumn();
								DrawEqCommitValue(stats.EQCommitSizeMB);

								imgui_TableNextColumn();
								imgui_Text(stats.TimeRunning);
							}
						}
					}
					// Summary at the bottom
					imgui_Separator();
					List<MemoryStats> summaryStats= _memoryStats;
				
					if (summaryStats.Count > 0)
					{
						double totalCSharp = summaryStats.Sum(x => x.CSharpMemoryMB);
						double totalEQ = summaryStats.Sum(x => x.EQCommitSizeMB);

						imgui_Text($"Total Characters: {summaryStats.Count}");
						imgui_SameLine();
						imgui_Text($"Total C# Memory: {totalCSharp:N2} MB");
						imgui_SameLine();
						imgui_Text($"Total EQ Commit: {totalEQ:N2} MB");
					}
					else
					{
						imgui_Text("No memory statistics available. Use /e3memstats to collect data.");
					}

					imgui_Separator();
					if (imgui_CollapsingHeader("EQ Commit Severity Legend", (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen | ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_FramePadding)))
					{
						RenderSeverityLegend(false);
					}
				}
				RenderTopicSubscriptionPopup();
			}
			finally
			{
				E3ImGUI.PopCurrentTheme();
			}
		}

		private static void RenderTopicSubscriptionPopup()
		{
			if (!imgui_Begin_OpenFlagGet(_TopicPopupWindowName)) return;

			imgui_SetNextWindowSizeWithCond(500, 350, (int)ImGuiCond.FirstUseEver);
			const ImGuiWindowFlags popupFlags = ImGuiWindowFlags.ImGuiWindowFlags_NoDocking;
			using (var popup = ImGUIWindow.Aquire())
			{
				if (!popup.Begin(_TopicPopupWindowName, (int)popupFlags)) return;

				string characterName = E3.CurrentName ?? string.Empty;
				var sharedClient = NetMQServer.SharedDataClient;
				if (sharedClient == null)
				{
					imgui_Text("Shared data client is not available.");
					if (imgui_Button("Close"))
					{
						imgui_Begin_OpenFlagSet(_TopicPopupWindowName, false);
					}
					return;
				}

				if (string.IsNullOrEmpty(characterName))
				{
					imgui_Text("Current character name is unknown.");
					if (imgui_Button("Close"))
					{
						imgui_Begin_OpenFlagSet(_TopicPopupWindowName, false);
					}
					return;
				}

				if (!sharedClient.TopicUpdates.TryGetValue(characterName, out var topics) || topics == null || topics.IsEmpty)
				{
					imgui_Text($"Character: {characterName}");
					imgui_Text("No active topic subscriptions detected.");
					if (imgui_Button("Close"))
					{
						imgui_Begin_OpenFlagSet(_TopicPopupWindowName, false);
					}
					return;
				}

				var topicSnapshot = topics.ToArray();
				imgui_Text($"Character: {characterName}");
				imgui_Text($"Active Topics: {topicSnapshot.Length:N0}");
				imgui_Separator();

				Int64 now = Core.StopWatch?.ElapsedMilliseconds ?? 0;
				using (var child = ImGUIChild.Aquire())
				{
					if (child.BeginChild("TopicSubscriptionList", 0f, 220f, (int)(ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding), 0))
					{
						foreach (var topic in topicSnapshot.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
						{
							var entry = topic.Value;
							string detail = FormatTopicDetail(entry, now);
							imgui_Text(string.IsNullOrEmpty(detail) ? topic.Key : $"{topic.Key} {detail}");
						}
					}
				}

				imgui_Separator();
				if (imgui_Button("Close"))
				{
					imgui_Begin_OpenFlagSet(_TopicPopupWindowName, false);
				}
			}
		}

		private static string FormatTopicDetail(ShareDataEntry entry, long now)
		{
			if (entry == null) return string.Empty;
			long ageMs = (now > 0 && entry.LastUpdate > 0) ? Math.Max(0, now - entry.LastUpdate) : -1;
			double seconds = ageMs >= 0 ? ageMs / 1000d : -1;
			int payloadLength = entry.Data?.Length ?? 0;
			if (seconds < 0)
			{
				return payloadLength > 0 ? $"(payload {payloadLength:N0} chars)" : string.Empty;
			}

			string payloadText = payloadLength > 0 ? $", payload {payloadLength:N0} chars" : string.Empty;
			return $"(updated {seconds:N1}s ago{payloadText})";
		}

		private static void DrawEqCommitValue(double eqCommitMb)
		{
			var (r, g, b) = GetEqCommitSeverityColor(eqCommitMb);
			imgui_TextColored(r, g, b, 1.0f, eqCommitMb.ToString("N2"));
		}

		private static (float r, float g, float b) GetEqCommitSeverityColor(double eqCommitMb)
		{
			double eqCommitGb = eqCommitMb / 1024d;
			foreach (var band in _eqCommitSeverityBands)
			{
				if (eqCommitGb >= band.MinGb && eqCommitGb < band.MaxGb)
				{
					return (band.R, band.G, band.B);
				}
			}

			return (0.9f, 0.9f, 0.9f);
		}

		private static void RenderSeverityLegend(bool includeHeader = true)
		{
			if (includeHeader)
			{
				imgui_Text("EQ Commit severity legend:");
			}
			foreach (var band in _eqCommitSeverityBands)
			{
				imgui_TextColored(band.R, band.G, band.B, 1.0f, $"  {band.Label}");
			}
		}
		private static int ParseInt(string value)
		{
			if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
			return -1;
		}

		private static void DrawBacklogCell(MemoryStats stats)
		{
			DrawBacklogLine("Cmd", stats.CommandQueueDepth, stats.CommandQueueDrops);
			DrawBacklogLine("IMGUI", stats.ImguiQueueDepth, stats.ImguiQueueDrops);
			DrawBacklogLine("Router", stats.RouterRequestDepth, stats.RouterDropCount, stats.RouterResponseDepth);
			DrawBacklogLine("Pub", stats.PubQueueDepth, stats.PubQueueDrops);
		}

		private static void DrawBacklogLine(string label, int depth, int drops, int secondary = -1)
		{
			string depthText = depth >= 0 ? depth.ToString("N0", CultureInfo.InvariantCulture) : "-";
			string dropText = drops >= 0 ? drops.ToString("N0", CultureInfo.InvariantCulture) : "-";
			string extra = secondary >= 0 ? $"/{secondary.ToString("N0", CultureInfo.InvariantCulture)}" : string.Empty;
			imgui_Text($"{label}: {depthText}{extra} (drops {dropText})");
		}

		public class MemoryStats
		{
			public string CharacterName { get; set; } = string.Empty;
			public double CSharpMemoryMB { get; set; }
			public double EQCommitSizeMB { get; set; }
			public string TimeRunning { get; set; } = string.Empty;
			public int CommandQueueDepth { get; set; } = -1;
			public int CommandQueueDrops { get; set; } = 0;
			public int ImguiQueueDepth { get; set; } = -1;
			public int ImguiQueueDrops { get; set; } = 0;
			public int RouterRequestDepth { get; set; } = -1;
			public int RouterResponseDepth { get; set; } = -1;
			public int RouterDropCount { get; set; } = 0;
			public int PubQueueDepth { get; set; } = -1;
			public int PubQueueDrops { get; set; } = 0;

			public MemoryStats()
			{
			}

			public MemoryStats(string characterName, double cSharpMemoryMB, double eqCommitSizeMB)
			{
				CharacterName = characterName;
				CSharpMemoryMB = cSharpMemoryMB;
				EQCommitSizeMB = eqCommitSizeMB;
			}
		}
	}
}
