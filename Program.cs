namespace liebo;

using System.Net;
using System.Net.NetworkInformation;
using Discord;
using Discord.WebSocket;

public class Program
{
    //Variablen
    private static DiscordSocketClient _client;
    public static string version;
    public static string bot_token;
    public static SocketGuild guild;
    public static SocketTextChannel logchannel;
    public static SocketTextChannel welcomechannel;
    public static SocketTextChannel jobchannel;
    private static HttpListener healtcheck_host = new HttpListener();
    public static void Main(string[] args) => new Program().Startup().GetAwaiter().GetResult();

    public async Task Startup()
    {
        DotNetEnv.Env.Load();
        bot_token = Environment.GetEnvironmentVariable("bot_token");
        //Set Version
        await SetVersion();

        //Startup
        Console.WriteLine("\n");
        string ascii_logo = @"
+------------------------------------------------------------+
|      :::        ::::::::::: :::::::::: :::::::::   ::::::::|
|     :+:            :+:     :+:        :+:    :+: :+:    :+:|
|    +:+            +:+     +:+        +:+    +:+ +:+    +:+ |
|   +#+            +#+     +#++:++#   +#++:++#+  +#+    +:+  |
|  +#+            +#+     +#+        +#+    +#+ +#+    +#+   |
| #+#            #+#     #+#        #+#    #+# #+#    #+#    |
|########## ########### ########## #########   ########      |
+------------------------------------------------------------+
        ";
        Console.WriteLine(ascii_logo); //made with https://www.asciiart.eu/text-to-ascii-art
        Console.WriteLine($"Liebo (v{version}) is starting...");
        //Set Client
        var config = new DiscordSocketConfig()
        {
            GatewayIntents = GatewayIntents.All,
            LogGatewayIntentWarnings = false,
            AlwaysDownloadUsers = true,
            ResponseInternalTimeCheck = false,
            MessageCacheSize = 5 //number of messages to be cached
        };

        _client = new DiscordSocketClient(config);

        //Subscribe to Events
        _client.SlashCommandExecuted += SlashCommandHandler;
        _client.UserJoined += UserJoinedHandler;
        _client.UserLeft += UserLeftHandler;
        _client.ModalSubmitted += ModalSubmittedHandler;
        _client.MessageReceived += MessageReceivedHandler;

        //Connect to Discord
        TaskCompletionSource<bool> readyTcs = new TaskCompletionSource<bool>();
        _client.Ready += () => //für warten bis ready
        {
            readyTcs.SetResult(true);
            return Task.CompletedTask;
        };

        await _client.LoginAsync(TokenType.Bot, bot_token);
        await _client.StartAsync();
        await readyTcs.Task;

        //Set Variables
        await SetVariables();

        //Register Commands
        await RegisterCommands();

        //Start Healthcheck
        HealthCheck();

        Console.WriteLine($"Liebo started successfull.");
        await Task.Delay(-1);
    }

    private async Task SetVersion()
    {
        try
        {
            version = File.ReadAllText("version.txt");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: Version could not be read:\nError: {ex.Message}");
            Environment.Exit(0);
        }
    }

    public async Task SetVariables()
    {
        guild = _client.GetGuild(ulong.Parse(Environment.GetEnvironmentVariable("guild_id")));
        logchannel = _client.GetChannel(ulong.Parse(Environment.GetEnvironmentVariable("logchannel_id"))) as SocketTextChannel;
        welcomechannel = _client.GetChannel(ulong.Parse(Environment.GetEnvironmentVariable("welcomechannel_id"))) as SocketTextChannel;
        jobchannel = _client.GetChannel(ulong.Parse(Environment.GetEnvironmentVariable("jobchannel_id"))) as SocketTextChannel;
    }

    public async Task RegisterCommands()
    {
        //create slash cmds

        //debug cmd
        var debugcmd = new SlashCommandBuilder();
        debugcmd.WithName("debug");
        debugcmd.WithDescription("Debug CMD for development and maintenance");
        debugcmd.AddOption("cmd", ApplicationCommandOptionType.String, "cmd", isRequired: true);

        //roadmap cmd
        var roadmapcmd = new SlashCommandBuilder();
        roadmapcmd.WithName("roadmap");
        roadmapcmd.WithDescription("Get the Roadmap");

        //jobs cmd
        var jobscmd = new SlashCommandBuilder();
        jobscmd.WithName("jobs");
        jobscmd.WithDescription("Post a job offer (For freelancers looking for work)");

        //build message/user context command
        //User Commands
        //var usercmd = new UserCommandBuilder();
        //usercmd.WithName("XX");

        //Message Commands
        //var msgcmd = new MessageCommandBuilder();
        //msgcmd.WithName("XX");

        //build all commands
        await guild.BulkOverwriteApplicationCommandAsync(new ApplicationCommandProperties[]
        {
            //slash cmds
            debugcmd.Build(),
            roadmapcmd.Build(),
            jobscmd.Build(),

            //context cmds
            //usercmd.Build(),
            //msgcmd.Build(),
        });
    }

    public static void HealthCheck()
    {
        healtcheck_host.Prefixes.Add("http://localhost:5000/");

        healtcheck_host.Start();

        Task.Run(async () =>
        {
            while (true)
            {
                var context = await healtcheck_host.GetContextAsync();
                var response = context.Response;

                var healthStatus = _client.ConnectionState == Discord.ConnectionState.Connected ? "OK" : "FAIL";
                var content = $"{healthStatus}";

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(content);
                response.ContentLength64 = buffer.Length;

                using (var output = response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }

                response.Close();
            }
        });
    }

    //Bot Functions
    private async Task SlashCommandHandler(SocketSlashCommand command)
    {
        //handle debug cmd
        if(command.CommandName == "debug")
        {
            string cmd = command.Data.Options.FirstOrDefault(x => x.Name == "cmd")?.Value.ToString();
            await command.DeferAsync(ephemeral: true);

            if(cmd == "test")
            {
                PingReply pingreply;
                using (var pinger = new Ping())
                {
                    pingreply = pinger.Send("1.1.1.1", 1000);
                }

                await command.FollowupAsync( 
                    $"`Status:` active and operating\n" +
                    $"`Discord Connection State:` {_client.ConnectionState}\n" +
                    $"`Ping to Cloudflare (Connection Test):` {pingreply.Status} ({pingreply.RoundtripTime}ms)\n" +
                    $"`Time (UTC):` {DateTime.UtcNow.ToString("HH:mm:ss")}, {DateTime.UtcNow.ToString("dd.MM.yyyy")}\n"
                );
                return;
            }

            if(cmd == "join")
            {
                await UserJoinedHandler(guild.GetUser(command.User.Id));
                await command.FollowupAsync("done");
                return;
            }

            if(cmd == "leave")
            {
                await UserLeftHandler(guild, guild.GetUser(command.User.Id));
                await command.FollowupAsync("done");
                return;
            }
        }

        //handle roadmap cmd
        if(command.CommandName == "roadmap")
        {
            var roadmap_embed = new EmbedBuilder
            {
                Title = "What features are currently in development for LibreChat?",
                Description = "Click on the button below to get to the current Roadmap!",
                Color = Color.Blue,
            }
            .Build();

            var roadmap_button = new ComponentBuilder();
            roadmap_button.WithButton(new ButtonBuilder()
            {
                Label = "Roadmap 🚀",
                Url = Environment.GetEnvironmentVariable("roadmap_link"),
                Style = ButtonStyle.Link,
            });

            await command.RespondAsync(embed: roadmap_embed, components: roadmap_button.Build(), ephemeral: true);
        }

        //handle jobs cmd
        if(command.CommandName == "jobs")
        {
            /*if(command.Channel.Id != jobchannel.Id)
            {
                var wrongchannel_embed = new EmbedBuilder
                {
                    Title = "You are in the wrong Channel!",
                    Description = $"For Job offers, switch the Channel to {jobchannel.Mention}",
                    Color = Color.Red,
                }
                .Build();
                await command.RespondAsync(embed: wrongchannel_embed, ephemeral: true);
                return;
            }*/

            var joboffer_input = new ModalBuilder()
            .WithTitle("Job Offer")
            .WithCustomId("joboffer_modal")
            .AddTextInput("What is your designation?", "designation_input", TextInputStyle.Short, required: true, placeholder:"e.g. Freelancer")
            .AddTextInput("Which programming languages do you know?", "p_languages_input", TextInputStyle.Paragraph, required: true, placeholder:"e.g. Python, React, MySQL or C#")
            .AddTextInput("How much experience do you already have?", "experience_input", TextInputStyle.Short, required: true, placeholder:"e.g. 4 Years")
            .AddTextInput("Do you have a Website? (only if yes)", "website_input", TextInputStyle.Short, required: false, placeholder:"e.g. https://mycoolsite.com")
            .AddTextInput("Would you like to add anything else?", "other_input", TextInputStyle.Paragraph, required: false);

            await command.RespondWithModalAsync(joboffer_input.Build());

            //continue in modal funktion
        }
    }

    private async Task ModalSubmittedHandler(SocketModal modal)
    {
        if(modal.Data.CustomId == "joboffer_modal")
        {
            var job_embed = new EmbedBuilder
            {
                Description = @$"# {modal.Data.Components.First(x => x.CustomId == "designation_input").Value} looking for assignments!
                Hi, I'm {modal.User.Mention} and I'm happy to offer you my help with your project!",
                Color = Color.Blue,
                Footer = new EmbedFooterBuilder().WithText($"Regards, your {modal.User.GlobalName}!").WithIconUrl(modal.User.GetAvatarUrl()),
                //Timestamp = (DateTimeOffset.Now)
            }
            .AddField("Which languages do I know?", modal.Data.Components.First(x => x.CustomId == "p_languages_input").Value)
            .AddField("How much experience do I have?", $"{modal.Data.Components.First(x => x.CustomId == "experience_input").Value}{(modal.Data.Components.First(x => x.CustomId == "other_input").Value == "" ? "" : $"\n\n**{modal.Data.Components.First(x => x.CustomId == "other_input").Value}**")}\n\nHave I got you interested? Then write me a DM here on Discord! I can't wait to hear from you!")
            .Build();

            if(modal.Data.Components.First(x => x.CustomId == "website_input").Value != "")
            {
                var website_button = new ComponentBuilder();
                website_button.WithButton(new ButtonBuilder()
                {
                    Label = "Go to my Website! 🌐",
                    Url = modal.Data.Components.First(x => x.CustomId == "website_input").Value,
                    Style = ButtonStyle.Link,
                });

                await jobchannel.SendMessageAsync(embed: job_embed, components: website_button.Build());
            }
            else
            {
                await jobchannel.SendMessageAsync(embed: job_embed);
            }

            await modal.RespondAsync("done", ephemeral: true);
        }
    }

    private async Task MessageReceivedHandler(SocketMessage message)
    {
        if(message.Channel.Id == jobchannel.Id)
        {
            await message.DeleteAsync();

            //log
            var log_embed = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder().WithName("Message deleted"),
                Title = $"A message has been deleted in {jobchannel.Mention}.",
                Description = $"Reason: The users should use the \"/jobs\" command.\nContent of the deleted message from {message.Author.Mention}:```\n{message.Content.Replace("`", "`​")}```", //caution! here are “zero-width blanks”
                Color = Color.Red,
            }
            .Build();

            await logchannel.SendMessageAsync(embed: log_embed);
        }
    }

    private async Task UserJoinedHandler(SocketGuildUser user)
    {
        //welcome message
        var welcome_embed = new EmbedBuilder
        {
            Author = new EmbedAuthorBuilder().WithName("Welcome!"),
            Title = "A new user has joined! :heart_eyes:",
            ThumbnailUrl = user.GetAvatarUrl(),
            Description = $"Welcome on {guild.Name}, {user.Mention}!\n-# We are now {guild.MemberCount} users • joined <t:{new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()}:R>",
            Color = Color.Green,
        }
        .Build();

        await welcomechannel.SendMessageAsync(embed: welcome_embed);

        //dm message
        var welcome_user_embed = new EmbedBuilder
        {
            Description = @$"# Welcome on {guild.Name}, {guild.GetUser(user.Id).DisplayName}! 👋
            I'm {_client.CurrentUser.Username}, and I'm here to make sure everything works and everyone feels comfortable.  
            If you have any questions, feel free to ask any time!
            Have fun on {guild.Name} 😊",
            Color = Color.Green,
            Footer = new EmbedFooterBuilder().WithText($"Regards, {_client.CurrentUser.Username} and the LibreChat Team"),
            //Timestamp = (DateTimeOffset.Now)
        }
        .Build();

        await user.SendMessageAsync(embed: welcome_user_embed);
    }

    private async Task UserLeftHandler(SocketGuild guild, SocketUser user)
    {
        var goodbye_embed = new EmbedBuilder
        {
            Author = new EmbedAuthorBuilder().WithName("Goodbye..."),
            Title = $"A user has left the server. :pensive:",
            ThumbnailUrl = user.GetAvatarUrl(),
            Description = $"{user.Mention} has left...\n-# We are now {guild.MemberCount} users • left <t:{new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()}:R>",
            Color = Color.DarkRed,
        }
        .Build();

        await welcomechannel.SendMessageAsync(embed: goodbye_embed);
    }
}
