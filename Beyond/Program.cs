using Beyond;
using Discord.Interactions;
using Discord.WebSocket;
using dotenv.net;
using Microsoft.Extensions.DependencyInjection;
using Discord;

class Program
{
    private readonly IServiceProvider _serviceProvider;
    private IDictionary<string, string> _environment;
    private DiscordSocketClient _client;
    private InteractionService _interactionService; 

    public Program()
    {
        _environment = DotEnv.Read();
        _serviceProvider = CreateServices();
        _client = _serviceProvider.GetService<DiscordSocketClient>()!;
        _interactionService = _serviceProvider.GetService<InteractionService>()!;
    }

    private static IServiceProvider CreateServices()
    {
        var collection = new ServiceCollection()
            //.AddSingleton(config)
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton<InteractionService>()
            .AddSingleton<BeyondCommandService>();

        return collection.BuildServiceProvider();
    }
    private Task LogError(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    private async Task OnClientReady()
    {
        var commands = (await _interactionService.AddModuleAsync<BeyondCommands>(_serviceProvider)).SlashCommands.ToArray();
        if (commands is null) throw new Exception("Could not get slash commands from BeyondCommands");

        // Enable - on debug, send these commands to our test server, otherwise put on global.
#if DEBUG
        var guildId = ulong.Parse(_environment["GUILD_ID"]);
        var guild = _client.GetGuild(guildId);
        await _interactionService.AddCommandsToGuildAsync(guild, commands: commands);
#else
        await _interactionService.AddCommandsGloballyAsync(commands: commands);
#endif
    }

    private async Task OnInteractionCreate(SocketInteraction s)
    {
        var ctx = new SocketInteractionContext(_client, s);
        await _interactionService.ExecuteCommandAsync(ctx, _serviceProvider);
    }

    public static Task Main(String[] args) => new Program().Start(args);

    public async Task Start(String[] args)
    {
#if DEBUG
        var token = _environment["APPLICATION_TOKEN_DEBUG"];
#else
        var token = _environment["APPLICATION_TOKEN"];
#endif
        if (token is null) throw new Exception("Could not find token in environment!");

        // Starting up the bot and adding events.
        _client.Log += LogError;
        _client.Ready += OnClientReady;
        _client.InteractionCreated += OnInteractionCreate;
        _interactionService.Log += LogError;
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }
}