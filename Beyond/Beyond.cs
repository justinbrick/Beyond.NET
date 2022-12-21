using Beyond;
using Discord.Interactions;
using Discord.WebSocket;
using dotenv.net;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Amazon.DynamoDBv2.Model;
using System.Linq;
using System.Collections.Generic;
using System.Data;

class BeyondBot
{
    private readonly IServiceProvider _serviceProvider;
    private IDictionary<string, string> _environment;
    private DiscordSocketClient _client;
    private InteractionService _interactionService;
    private BeyondDatabase _database;
    private BeyondElectionService _electionService;

    // I think this goes against the dependency injection design - will have to review after more research into this topic.
    public static readonly BeyondBot Instance = new();

    // The name of the Gumby role
    public const string GumbyRoleName = "Gumby of the Month";
    // The color of the Gumby role
    public static readonly Color GumbyColor = new Color(191, 24, 226);

    // The permissions that Gumby will have by default. Basically given the capabilities of an admin, without being given the ability to mess with the server itself.
    public static readonly GuildPermissions GumbyGuildPermissions = new(
            manageMessages: true,
            manageChannels: true,
            manageEmojisAndStickers: true,
            manageRoles: true,
            manageNicknames: true,
            moderateMembers: true,
            attachFiles: true,
            kickMembers: true,
            viewAuditLog: true,
            createInstantInvite: true,
            viewGuildInsights: true
        );
    // The name of the category channel which all the template channels are stored in.
    public const string BeyondCategoryName = "beyond";
    // The permissions which the Gumby is not allowed to have. These are for the default channels only, and they will still be able to manage the ones that are not defaults.
    public static readonly OverwritePermissions GumbyDefaultChannelRestrictions = new(manageChannel: PermValue.Deny);
    // A list of database identifiers, as well as the names used as display names for those channels.
    // TODO: Make configurable, if possible.
    public static readonly List<(string, string)> GuildMappings = new()
    {
        ("general", "general"),
        ("development", "development"),
        ("bot", "bot"),
        ("rules", "rules")
    };
    
    public BeyondBot()
    {
        _environment = DotEnv.Read();
        _serviceProvider = CreateServices();
        _client = _serviceProvider.GetService<DiscordSocketClient>()!;
        _interactionService = _serviceProvider.GetService<InteractionService>()!;
        _database = _serviceProvider.GetService<BeyondDatabase>()!;
        _electionService = _serviceProvider.GetService<BeyondElectionService>()!;
    }

    private static IServiceProvider CreateServices()
    {
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers | GatewayIntents.Guilds
        };

        var collection = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton<InteractionService>()
            .AddSingleton<BeyondDatabase>()
            .AddSingleton<BeyondElectionService>();

        return collection.BuildServiceProvider();
    }
    private Task LogError(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    public async Task VerifyGuild(Dictionary<string, AttributeValue> guildResource, SocketGuild guild) {

        Dictionary<string, AttributeValueUpdate> changes = new();
        IRole? gumbyRole = guild.GetRole(guildResource.TryGetValue("gumby", out var gumbyRoleId) ? ulong.Parse(gumbyRoleId.N) : 0);
        if (gumbyRole is null)
        {
            gumbyRole = guild.Roles.FirstOrDefault(role => role.Name == GumbyRoleName);
            gumbyRole ??= await guild.CreateRoleAsync(GumbyRoleName, permissions: GumbyGuildPermissions, color: GumbyColor);
            var value = new AttributeValue { N = gumbyRole.Id.ToString() };
            changes["gumby"] = new AttributeValueUpdate
            {
                Action = "PUT",
                Value = value
            };
            guildResource["gumby"] = value;
        }
        if (gumbyRole.Name != GumbyRoleName || gumbyRole.Color != GumbyColor)
        {
            await gumbyRole.ModifyAsync(role =>
            {
                role.Name = GumbyRoleName;
                role.Color = GumbyColor;
            });
        }

        // Get the category channel from a guild ID. If it cannot find it, then try and replace it with channel that's already in the guild named the same thing (if there is one) 
        ICategoryChannel? beyondCategoryChannel = guild.GetCategoryChannel(guildResource.TryGetValue(BeyondCategoryName, out var categoryChannelId) ? ulong.Parse(categoryChannelId.N) : 0);
        if (beyondCategoryChannel is null)
        {
            beyondCategoryChannel = guild.CategoryChannels.FirstOrDefault(channel => channel.Name == BeyondCategoryName);
            beyondCategoryChannel ??= await guild.CreateCategoryChannelAsync(BeyondCategoryName, (channelProperties) =>
            {
                channelProperties.PermissionOverwrites = new List<Overwrite>()
                {
                    new Overwrite(gumbyRole.Id, PermissionTarget.Role, GumbyDefaultChannelRestrictions)
                };
            });
            var value = new AttributeValue { N = beyondCategoryChannel.Id.ToString() };
            changes[BeyondCategoryName] = new AttributeValueUpdate
            {
                Action = "PUT",
                Value = value
            };
            guildResource[BeyondCategoryName] = value;
        }
        // If this category is not properly configured, then let's fix the attributes so it's properly placed.
        if (beyondCategoryChannel.Position != 0 || beyondCategoryChannel.Name != BeyondCategoryName)
        {
            await beyondCategoryChannel.ModifyAsync(properties =>
            {
                properties.Name = BeyondCategoryName;
                properties.Position = 0;
            });
        }
        // Iterate through the list of mappings and channel names to see if they exist - if they don't, create / find one.
        for (int i = 0; i < GuildMappings.Count; ++i)
        {
            var (resourceName, channelName) = GuildMappings[i];
            // Try and get the channel from it's ID, or one from the guild with the same name.
            ITextChannel? channel = guild.GetTextChannel(guildResource.TryGetValue(resourceName, out var channelId) ? ulong.Parse(channelId.N) : 0);
            if (channel is null)
            {
                channel = guild.TextChannels.FirstOrDefault(channel => channel.Name == channelName);
                channel ??= await guild.CreateTextChannelAsync(channelName, (properties) =>
                {
                    properties.CategoryId = beyondCategoryChannel.Id;
                    properties.PermissionOverwrites = new List<Overwrite>()
                    {
                        new Overwrite(gumbyRole.Id, PermissionTarget.Role, GumbyDefaultChannelRestrictions)
                    };
                });
                var value = new AttributeValue { N = channel.Id.ToString() };
                changes[resourceName] = new AttributeValueUpdate
                {
                    Action = "PUT",
                    Value = value
                };
                guildResource[resourceName] = value;
            }
            // This is a redundant request in the case that there actually is a channel that fits the conditions. We need to make sure that these channels are current & available.
            if (channel.CategoryId != beyondCategoryChannel.Id || channel.Name != channelName || channel.Position != i)
            {
                await channel.ModifyAsync(properties =>
                {
                    properties.CategoryId = beyondCategoryChannel.Id;
                    properties.Name = channelName;
                    properties.Position = i;
                    properties.PermissionOverwrites = new List<Overwrite>()
                    {
                        new Overwrite(gumbyRole.Id, PermissionTarget.Role, GumbyDefaultChannelRestrictions)
                    };
                });
            }
            
        }
        // If there's been a change, we need to re-put this item back into the list.
        if (changes.Count > 0)
        {
            var updateRequest = new UpdateItemRequest
            {
                Key =
                {
                    ["guild"] = new AttributeValue{ N=guild.Id.ToString() },
                    ["tag"] = new AttributeValue { S = "information" }
                },
                AttributeUpdates = changes
            };
            var updateResponse = await _database.UpdateItemAsync(updateRequest);
        }
    }    

    private async Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        var guildObject = await _database.GetGuildInformation(guild);
        var gumbyId = guildObject.TryGetValue("gumby", out var gumby) ? ulong.Parse(gumby.N) : 0;
        // In this scenario, the GOTM just left. We need to recalculate for a new GOTM.
        if (user.Id == gumbyId) await _electionService.CreateGumbyElection(guild);
    }
    private async Task OnClientReady()
    {
        // Register slash commands.
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

    public static Task Main(String[] args) => BeyondBot.Instance.Start(args);

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
        _client.UserLeft += OnUserLeft;
        _interactionService.Log += LogError;
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }
}