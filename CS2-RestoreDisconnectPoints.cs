using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using System.Text.Json.Serialization;
using static CounterStrikeSharp.API.Core.Listeners;

namespace CS2_RestoreDisconnectStats;

public class CS2_RestoreDisconnectStatsConfig : BasePluginConfig
{
	[JsonPropertyName("ConfigVersion")] public override int Version { get; set; } = 1;
	[JsonPropertyName("Minutes")] public int Minutes { get; set; } = 5;
}

public class CS2_RestoreDisconnectStats : BasePlugin, IPluginConfig<CS2_RestoreDisconnectStatsConfig>
{
	public CS2_RestoreDisconnectStatsConfig Config { get; set; } = new();

	public Dictionary<ulong, PlayerStats> SavedPlayerStats = [];
	public CCSGameRules? GameRules;
	public static int ExpireTime;
	public int startMoney;

	public override string ModuleName => "CS2-RestoreDisconnectStats";
	public override string ModuleVersion => "1.0.0 (BETA)";
	public override string ModuleAuthor => "daffyy";

	public override void Load(bool hotReload)
	{
		RegisterListener<OnClientDisconnect>(OnClientDisconnect);
		RegisterListener<OnMapStart>(OnMapStart);

		AddTimer(60, () =>
		{
			RemoveExpiredStats();
		}, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
	}

	public void OnConfigParsed(CS2_RestoreDisconnectStatsConfig config)
	{
		if (config.Minutes < 1)
			config.Minutes = 1;

		ExpireTime = config.Minutes;
		Config = config;
	}

	public void OnMapStart(string mapname)
	{
		SavedPlayerStats.Clear();

		AddTimer(1, () =>
		{
			GameRules ??= Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;
			startMoney = ConVar.Find("mp_startmoney")?.GetPrimitiveValue<int>() ?? 800;
		});
	}

	public void OnClientDisconnect(int playerSlot)
	{
		var player = Utilities.GetPlayerFromSlot(playerSlot);

		if (player == null || !player.IsValid || player.ActionTrackingServices == null || player.IsBot)
			return;

		SavedPlayerStats[player.SteamID] = new PlayerStats
		{
			Kills = player.ActionTrackingServices.MatchStats.Kills,
			Deaths = player.ActionTrackingServices.MatchStats.Deaths,
			Assists = player.ActionTrackingServices.MatchStats.Assists,
			Damage = player.ActionTrackingServices.MatchStats.Damage,
			Score = player.Score,
			Mvps = player.MVPs,
			Money = player.InGameMoneyServices!.Account,
		};
	}

	[GameEventHandler]
	public HookResult EventPlayerActivate(EventPlayerActivate @event, GameEventInfo _)
	{
		var player = @event.Userid;

		if (player == null || !player.IsValid || player.IsBot || player.ActionTrackingServices == null || !SavedPlayerStats.TryGetValue(player.SteamID, out PlayerStats? savedStats) || savedStats == null)
			return HookResult.Continue;

		player.ActionTrackingServices.MatchStats.Kills = savedStats.Kills;
		player.ActionTrackingServices.MatchStats.Deaths = savedStats.Deaths;
		player.ActionTrackingServices.MatchStats.Assists = savedStats.Assists;
		player.ActionTrackingServices.MatchStats.Damage = savedStats.Damage;
		player.Score = savedStats.Score;
		player.MVPs = savedStats.Mvps;

		if (!IsPistolRound() && savedStats.Money > startMoney)
		{
			player.InGameMoneyServices!.Account = savedStats.Money;
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
		}

		return HookResult.Continue;
	}

	public void RemoveExpiredStats()
	{
		foreach (var key in SavedPlayerStats.Where(entry => entry.Value.ExpireAt < DateTime.Now).Select(entry => entry.Key).ToList())
		{
			SavedPlayerStats.Remove(key);
		}
	}

	public void ResetMoneyOnPistol()
	{
		foreach (var key in SavedPlayerStats.Where(entry => entry.Value.Money > startMoney).Select(entry => entry.Key).ToList())
		{
			SavedPlayerStats[key].Money = 0;
		}
	}

	[GameEventHandler(HookMode.Pre)]
	public HookResult EventRoundStart(EventRoundStart @event, GameEventInfo _)
	{
		if (IsPistolRound())
			ResetMoneyOnPistol();

		return HookResult.Continue;
	}

	private bool IsPistolRound()
	{
		var halftime = ConVar.Find("mp_halftime")!.GetPrimitiveValue<bool>();
		var maxrounds = ConVar.Find("mp_maxrounds")!.GetPrimitiveValue<int>();

		if (GameRules == null) return false;

		return GameRules.TotalRoundsPlayed == 0 || (halftime && maxrounds / 2 == GameRules.TotalRoundsPlayed) ||
			   GameRules.GameRestart;
	}
}

public class PlayerStats(int kills = 0, int deaths = 0, int assists = 0, int damage = 0, int score = 0, int mvps = 0, int money = 0, DateTime? expireAt = null)
{
	public int Kills { get; set; } = kills;
	public int Deaths { get; set; } = deaths;
	public int Assists { get; set; } = assists;
	public int Damage { get; set; } = damage;
	public int Score { get; set; } = score;
	public int Mvps { get; set; } = mvps;
	public int Money { get; set; } = money;
	public DateTime ExpireAt = expireAt ?? DateTime.Now.AddMinutes(CS2_RestoreDisconnectStats.ExpireTime);
}