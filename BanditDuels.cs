using Minecraft.Server.FourKit;
using Minecraft.Server.FourKit.Plugin;

using BanditDuels.Arenas;
using BanditDuels.Commands;
using BanditDuels.Config;
using BanditDuels.Duels;
using BanditDuels.Kits;
using BanditDuels.Listeners;
using BanditDuels.Lobby;
using BanditDuels.Party;
using BanditDuels.Permissions;
using BanditDuels.Queue;
using BanditDuels.Stats;
using BanditDuels.Ui;
using BanditDuels.World;

namespace BanditDuels;

public class BanditDuels : ServerPlugin
{
    public override string name    => "BanditDuels";
    public override string version => "0.1.0";
    public override string author  => "BanditVault";

    private static BanditDuels? _instance;
    public static BanditDuels Instance => _instance ?? throw new InvalidOperationException("BanditDuels not yet enabled");

    public PluginConfig Config { get; private set; } = null!;
    public DuelManager Manager { get; private set; } = null!;
    public ArenaRegistry Arenas { get; private set; } = null!;
    public KitRegistry Kits { get; private set; } = null!;
    public DuelHud Hud { get; private set; } = null!;
    public WorldKeeper? WorldKeeper { get; private set; }
    public ArenaGuardListener ArenaGuard { get; private set; } = null!;
    public LobbyGuardListener LobbyGuard { get; private set; } = null!;
    public LobbyManager Lobby { get; private set; } = null!;
    public MobCuller? MobCuller { get; private set; }
    public QueueManager? Queues { get; private set; }
    public PartyManager? Parties { get; private set; }
    public PendingResetManager? PendingResets { get; private set; }
    public StatsRepo? Stats { get; private set; }
    public AdminManager? Admins { get; private set; }
    public ArenaResetManager? ArenaResetter { get; private set; }

    public override void onEnable()
    {
        BundleLoader.Install();

        _instance = this;

        // Top-level config + arenas config. Defaults are written to disk on first run.
        Config = ConfigStore.loadPluginConfig();
        var arenasConfig = ConfigStore.loadArenasConfig();

        Arenas = new ArenaRegistry();
        Arenas.loadFromConfig(Config.WorldName, arenasConfig.Templates);

        Kits = new KitRegistry();
        foreach (var def in KitLoader.loadAll())
            Kits.register(new JsonKit(def));

        Hud = new DuelHud(this);
        Manager = new DuelManager(this, Arenas, Kits, Hud);

        PendingResets = new PendingResetManager();

        Stats = new StatsRepo();
        Stats.load();

        Admins = new AdminManager();
        Admins.load();

        // Migration notice: admins.json is no longer used for permission
        // checks. Surface the legacy names so the operator can transcribe
        // them into LCEPerms via /lp.
        if (Admins.all().Count > 0)
        {
            Console.WriteLine("[BanditDuels] NOTICE: admins.json is no longer used for permission checks.");
            Console.WriteLine("[BanditDuels]   Permissions now flow through LCEPerms. Legacy admins.json contains:");
            Console.WriteLine("[BanditDuels]     " + string.Join(", ", Admins.all()));
            Console.WriteLine("[BanditDuels]   To reproduce the old behavior, install LCEPerms and from console run:");
            Console.WriteLine("[BanditDuels]     lp group admin permission set banditduels.admin.* true");
            Console.WriteLine("[BanditDuels]     lp group admin permission set banditduels.bypass.lobby true");
            Console.WriteLine("[BanditDuels]     lp group admin permission set banditduels.bypass.chat true");
            foreach (var n in Admins.all())
                Console.WriteLine("[BanditDuels]     lp user " + n + " parent add admin");
            Console.WriteLine("[BanditDuels]   Then delete plugins/BanditDuels-data/admins.json.");
        }

        ArenaResetter = new ArenaResetManager();
        ArenaResetter.loadAllFromDisk(Arenas);
        ArenaResetter.start(this);

        FourKit.addListener(new DuelDeathListener(Manager));
        FourKit.addListener(new DuelQuitListener(Manager));
        FourKit.addListener(new DuelChatListener(Manager));
        ArenaGuard = new ArenaGuardListener(Manager);
        FourKit.addListener(ArenaGuard);

        Lobby = new LobbyManager();
        Lobby.load();
        LobbyGuard = new LobbyGuardListener(Lobby, Manager);
        // Apply the persisted lobbyguard toggles. Defaults true on fresh
        // installs; servers delegating lobby protection to WorldGuard (or
        // any other plugin) flip these to false in lobby.json.
        LobbyGuard.BlockProtectionEnabled      = Lobby.Config.BlockProtectionEnabled;
        LobbyGuard.BoundaryEnforcementEnabled  = Lobby.Config.BoundaryEnforcementEnabled;
        FourKit.addListener(LobbyGuard);
        LobbyGuard.start(this);

        Queues = new QueueManager(Manager, Kits);
        FourKit.addListener(new QueueListener(Queues));

        Parties = new PartyManager(Manager, Kits, Arenas);
        FourKit.addListener(new PartyListener(Parties));

        var executor = new DuelCommand(Manager, Kits, Arenas);
        FourKit.getCommand("duel").setExecutor(executor);
        FourKit.getCommand("duel").setDescription("Challenge a player to a duel");
        FourKit.getCommand("duel").setUsage("/duel <player> <kit> | /duel accept <player> | /duel deny <player>");

        Hud.start();

        if (Config.Features.LockWorldTime)
        {
            WorldKeeper = new WorldKeeper(this, Config.WorldName, Config.Features.NoonTicks);
            WorldKeeper.start();
        }

        if (Config.Features.MobCullEnabled)
        {
            MobCuller = new MobCuller(this, Config.WorldName,
                Config.Features.MobCullPeriodTicks, Config.Features.MobCullVoidY);
            MobCuller.start();
        }

        Console.WriteLine("[BanditDuels] enabled. World: " + Config.WorldName
            + ", Arenas: " + Arenas.count() + ", Kits: " + Kits.count()
            + ", TimeLock: " + Config.Features.LockWorldTime
            + ", MobCull: " + Config.Features.MobCullEnabled
            + ". Permissions via LCEPerms (or open if absent - see README).");
    }

    public override void onDisable()
    {
        Hud?.stop();
        Manager?.shutdown();
        Stats?.Dispose();
        Console.WriteLine("[BanditDuels] disabled.");
    }
}
