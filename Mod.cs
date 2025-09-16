using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Backend;
using MarketBot.Services;
using Backend.Business;
using BotLib.BotClient;
using BotLib.Generated;
using BotLib.Protocols;
using BotLib.Protocols.Queuing;
using MarketBot;
using Microsoft.Extensions.DependencyInjection;
using NQ;
using NQ.Router;
using NQutils;
using NQutils.Logging;
using NQutils.Sql;
using Orleans;


public class Mod
{
    public static IDuClientFactory RestDuClientFactory => serviceProvider.GetRequiredService<IDuClientFactory>();
    /// Use this to acess registered service
    protected static IServiceProvider serviceProvider;
    /// Use this to make gameplay calls, see "Interfaces/GrainGetterExtensions.cs" for what's available
    protected static IClusterClient orleans;
    /// Use this object for various data access/modify helper functions
    protected static IDataAccessor dataAccessor;

    /// Conveniance field for mods who need a single bot
    public static Client bot;
    /// Create or login a user, return bot client instance
    public static async Task<Client> CreateUser(string prefix, bool allowExisting = false, bool randomize = true)
    {
        string username = prefix;
        if (randomize)
        {
            // Do not use random utilities as they are using tests random (that is seeded), and we want to be able to start the same test multiple times
            Random r = new Random(Guid.NewGuid().GetHashCode());
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz";
            username = prefix + '-' + new string(Enumerable.Repeat(0, 127 - prefix.Length).Select(_ => chars[r.Next(chars.Length)]).ToArray());
        }
        LoginInformations pi = LoginInformations.BotLogin(username, Environment.GetEnvironmentVariable("BOT_LOGIN"), Environment.GetEnvironmentVariable("BOT_PASSWORD"));
        return await Client.FromFactory(RestDuClientFactory, pi, allowExising: allowExisting);
    }
    /// Setup everything, must be called once at startup
    public static async Task Setup(string configPath)
    {
        var services = new ServiceCollection();
        //services.RegisterCoreServices();
        var qurl = Environment.GetEnvironmentVariable("QUEUEING");
        if (qurl == "")
            qurl = "http://queueing:9630";

        services
            .AddSingleton<ISql, Sql>()
            .AddInitializableSingleton<IGameplayBank, GameplayBank>()
            .AddSingleton<ILocalizationManager, LocalizationManager>()
            .AddTransient<IDataAccessor, DataAccessor>()
            .AddLogging(logging => logging.Setup(logWebHostInfo: true))
            .AddOrleansClient("IntegrationTests")
            .AddHttpClient()
            .AddTransient<NQutils.Stats.IStats, NQutils.Stats.FakeIStats>()
            .AddSingleton<IQueuing, RealQueuing>(
                sp => new RealQueuing(qurl, sp.GetRequiredService<IHttpClientFactory>().CreateClient())
            )
            .AddSingleton<IDuClientFactory, BotLib.Protocols.GrpcClient.DuClientFactory>()
            .AddSingleton<Backend.Storage.IItemStorageService, Backend.Storage.ItemStorageService>()
            .AddSingleton<IRecipes, Recipes>();



        services.AddMarketBot(configPath);

        var sp = services.BuildServiceProvider();
        serviceProvider = sp;
        ClientExtensions.SetSingletons(sp);
        ClientExtensions.UseFactory(sp.GetRequiredService<IDuClientFactory>());
        await serviceProvider.StartServices();
        orleans = serviceProvider.GetRequiredService<IClusterClient>();
        dataAccessor = serviceProvider.GetRequiredService<IDataAccessor>();

        // Start metrics system manually since we don't use standard ASP.NET Core hosting
        var metricsCalculator = serviceProvider.GetRequiredService<BackgroundMetricsCalculator>();
        await metricsCalculator.StartAsync(default);
        Console.WriteLine("Metrics system started");
        
        Console.WriteLine("Creating BOT User");
        bot = await RefreshClient();
        Console.WriteLine("BOT User Created");
    }

    public static async Task<Client> RefreshClient()
    {
        return await CreateUser("trader", true, false);
    }

    public async Task Start()
    {
        try
        {
            await Loop();
        }
        catch (Exception e)
        {
            Console.WriteLine($"{e}\n{e.StackTrace}");
            throw;
        }
    }
    /// Override this with main bot code
    public virtual Task Loop()
    {
        return Task.CompletedTask;
    }
    /// Conveniance helper for running code forever
    public async Task SafeLoop(Func<Task> action, int exceptionDelayMs,
        Func<Task> reconnect)
    {
        while (true)
        {
            try
            {
                await action();
            }
            catch (NQutils.Exceptions.BusinessException be) when (be.error.code == NQ.ErrorCode.InvalidSession)
            {
                Console.WriteLine("reconnecting");
                await reconnect();
                await Task.Delay(exceptionDelayMs);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception in mod action: {e}\n{e.StackTrace}");
                await Task.Delay(exceptionDelayMs);
            }
        }
    }
    public async Task ChatDmOnMention(string key)
    {
        var listener = bot.Events.MessageReceived.Listener();
        while (true)
        {
            var msg = await listener.GetLastEventWait(mc => true, 1000000000);
            listener.Clear();
            if (msg.message.Contains(key))
            {
                await bot.Req.ChatMessageSend(new NQ.MessageContent
                {
                    channel = new NQ.MessageChannel
                    {
                        channel = MessageChannelType.PRIVATE,
                        targetId = msg.fromPlayerId,
                    },
                    message = "You wanted to talk to me?",
                });
            }
        }
    }
}