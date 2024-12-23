namespace liebo;

using System.ClientModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using OpenAI;
using OpenAI.Chat;
using urldetector;
using urldetector.detection;

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
    public static SocketTextChannel aichannel;
    public static SocketTextChannel statschannel;
    public static SocketRole link_approved_role;
    public static SocketRole star_role;
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
        _client.Ready += () => //wait until "ready"
        {
            readyTcs.SetResult(true);
            return Task.CompletedTask;
        };

        Console.WriteLine($"-> Login into Discord...");
        await _client.LoginAsync(TokenType.Bot, bot_token);
        Thread.Sleep(500); //Discord API is sometimes sensitive, so to avoid errors
        await _client.StartAsync();
        Console.WriteLine("-> Login successful!");
        await readyTcs.Task;

        //Set Variables
        await SetVariables();

        //Register Commands
        await RegisterCommands();

        //set bot account status
        await _client.SetCustomStatusAsync("answers your questions");

        //Start Healthcheck
        HealthCheck();

        //Start Userstatus logging
        LogOnlineUsersAsync();

        //Start Stats Channel
        UpdateStatsChannel();

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
        aichannel = _client.GetChannel(ulong.Parse(Environment.GetEnvironmentVariable("aichannel_id"))) as SocketTextChannel;
        statschannel = _client.GetChannel(ulong.Parse(Environment.GetEnvironmentVariable("statschannel_id"))) as SocketTextChannel;

        link_approved_role = guild.GetRole(ulong.Parse(Environment.GetEnvironmentVariable("linkapproved_roleid")));
        star_role = guild.GetRole(ulong.Parse(Environment.GetEnvironmentVariable("star_roleid")));
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

        //contribute cmd
        var contributecmd = new SlashCommandBuilder();
        contributecmd.WithName("contribute");
        contributecmd.WithDescription("Contribute to Librechat");

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
            contributecmd.Build(),

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
    private async void LogOnlineUsersAsync()
    {
        while (true)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.AddMinutes(1 - (now.Minute % 1)).AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond);
            var delay = nextRun - now;

            await Task.Delay(delay);

            using (var writer = new StreamWriter("onlinehistory.csv", true))
            {
                var onlineCount = guild.Users.Count(user => user.Status != UserStatus.Offline && !user.IsBot);
                await writer.WriteLineAsync($"{new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()},{onlineCount}");
            }

            //delete entries that are older than 24 hours
            var lines = await File.ReadAllLinesAsync("onlinehistory.csv");
            var filteredLines = lines.Where(line =>
            {
                var timestamp = long.Parse(line.Split(',')[0]);
                return DateTime.UtcNow.AddHours(-24) < DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            });
            await File.WriteAllLinesAsync("onlinehistory.csv", filteredLines);
        }
    }

    private async void UpdateStatsChannel()
    {
        while (true)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.AddMinutes(1 - (now.Minute % 1)).AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond);
            var delay = nextRun - now;

            await Task.Delay(delay);

            var stats_embed = new EmbedBuilder
            {
                Description = $"# LibreChat Discord Statistics:\n" +
                $"### {guild.Users.Count(user => user.Status != UserStatus.Offline && !user.IsBot)}/{guild.Users.Count(user => !user.IsBot)} Users are currently online\n (*{guild.Users.Count(user => user.Status != UserStatus.Offline && !user.IsBot && user.Roles.Any(role => role.Id == star_role.Id))} of them with a ⭐*)",
                ImageUrl = $"attachment://image.png",
                Color = Color.DarkBlue,
                Footer = new EmbedFooterBuilder().WithText($"Liebo v{version}"),
            }
            .Build();

            var messages = await statschannel.GetMessagesAsync().FlattenAsync();
            var message = messages.Last(message => message.Author.IsBot) as IUserMessage;
            
            await message.ModifyAsync(msg =>
            {
                msg.Content = "";
                msg.Embed = stats_embed;
                msg.Attachments = new[] { new FileAttachment(GetOnlineUsersPng(), "image.png") };
            });
        }
    }

    public MemoryStream GetOnlineUsersPng()
    {
        ScottPlot.Plot myPlot = new();

        var lines = File.ReadAllLines("onlinehistory.csv");

        // create sample data
        var dataX = new DateTime[lines.Length];
        var dataY = new double[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            dataX[i] = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[0])).DateTime;
            dataY[i] = double.Parse(parts[1]);
        }

        // add a scatter plot to the plot
        var sig = myPlot.Add.Scatter(dataX, dataY);
        // add a green data line
        sig.Color = new("#3ae132");

        myPlot.Axes.SetLimitsY(0, guild.MemberCount);

        var axis = myPlot.Axes.DateTimeTicksBottom();

        static string CustomFormatter(DateTime dt)
        {
            bool isMidnight = dt is { Hour: 0, Minute: 0, Second: 0 };
            return isMidnight
                ? DateOnly.FromDateTime(dt).ToString()
                : TimeOnly.FromDateTime(dt).ToString();
        }

        var tickGen = (ScottPlot.TickGenerators.DateTimeAutomatic)axis.TickGenerator;
        tickGen.LabelFormatter = CustomFormatter;

        ScottPlot.TickGenerators.NumericAutomatic tickGenY = new();
        tickGenY.TickDensity = 0.1;
        tickGenY.TargetTickCount = 1;
        myPlot.Axes.Left.TickGenerator = tickGenY;

        myPlot.Title("User Online History", 30);
        myPlot.XLabel("Time (UTC)");
        myPlot.YLabel("User Online");

        myPlot.FigureBackground.Color = new("#1c1c1e");

        myPlot.Grid.XAxisStyle.MinorLineStyle.Width = 4;
        myPlot.Grid.YAxisStyle.MinorLineStyle.Width = 1;

        myPlot.Grid.XAxisStyle.MajorLineStyle.Color = ScottPlot.Colors.White.WithAlpha(15);
        myPlot.Grid.YAxisStyle.MajorLineStyle.Color = ScottPlot.Colors.White.WithAlpha(15);
        myPlot.Grid.XAxisStyle.MinorLineStyle.Color = ScottPlot.Colors.White.WithAlpha(5);
        myPlot.Grid.YAxisStyle.MinorLineStyle.Color = ScottPlot.Colors.White.WithAlpha(5);

        myPlot.Axes.Color(new("#888888"));

        Byte[] bytes = myPlot.GetImageBytes(1000, 400, ScottPlot.ImageFormat.Png);
        return new MemoryStream(bytes);
    }

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
                    $"`Status:` active and operating (v{version})\n" +
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

            if(cmd == "debug")
            {
                //temp command for testing (does nothing in prod.)
                await command.FollowupAsync("done");
                return;
            }

            if(cmd == "install statschannel")
            {
                var embed = new EmbedBuilder
                {
                    Description = $"installing..",
                }
                .Build();

                await command.Channel.SendMessageAsync(embed: embed);
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

        //handle contribute cmd
        if(command.CommandName == "contribute")
        {
            var contribute_embed = new EmbedBuilder
            {
                Description = @"# Would you like to contribute to the development of Librechat?
*Nice! We are happy about every PR!*
### Are you familiar with TypeScript/Javascript? Great!
Then you can help us develop Librechat further!
You have an idea how Librechat could become even better? Perfect!
Create a PR and add your feature, and soon it will be in Librechat!
### You know your way around Nextra3 (and .mdx files)? Cool!
Then you can help to keep our documentation up to date and can document the functionality of new features and write instructions!
This will help new and old Librechat users to understand Librechat and make it more accessible!
### Not familiar with TypeScript/Javascript/Nextra3 or even programming in general?
No problem!
You can also support LibreChat by translating Librechat into your native language and make Librechat available to many more people!

**It doesn't matter how you participate in the development of Librechat. We are happy about every little idea! And with YOUR help, Librechat will continue to grow!**",
                Color = Color.Blue,
                Footer = new EmbedFooterBuilder().WithText("Click on the buttons below to find out more about how you can contribute!"),
            }
            .Build();

            var componentBuilder = new ComponentBuilder()
            .WithButton(new ButtonBuilder()
            {
                Label = "Development 🚀",
                Url = Environment.GetEnvironmentVariable("contribute_dev_link"),
                Style = ButtonStyle.Link,
            })
            .WithButton(new ButtonBuilder()
            {
                Label = "Documentation 📚",
                Url = Environment.GetEnvironmentVariable("contribute_docs_link"),
                Style = ButtonStyle.Link,
            })
            .WithButton(new ButtonBuilder()
            {
                Label = "Translation 🌍",
                Url = Environment.GetEnvironmentVariable("contribute_translate_link"),
                Style = ButtonStyle.Link,
            });

            await command.RespondAsync(embed: contribute_embed, components: componentBuilder.Build(), ephemeral: true);
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
        if(message.Channel.Id == jobchannel.Id && !message.Author.IsBot)
        {
            await message.DeleteAsync();

            //log
            var log_embed = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder().WithName("Message deleted"),
                Title = $"A message has been deleted in {jobchannel.Mention}",
                Description = $"Reason: The users should use the \"/jobs\" command.\nContent of the deleted message from {message.Author.Mention}:```\n{message.Content.Replace("`", "`​")}```", //caution! here are “zero-width blanks”
                Color = Color.Gold,
                Footer = new EmbedFooterBuilder().WithText($"Liebo v{version}"),
            }
            .Build();

            await logchannel.SendMessageAsync(embed: log_embed);
        }

        //link whitelist
        //link check -> deactivated/inactive and not finished
        if(!(message.Author as SocketGuildUser).Roles.Any(role => role.Id == link_approved_role.Id) && !message.Author.IsBot)
        {
            
            UrlDetector parser = new UrlDetector(message.CleanContent, UrlDetectorOptions.Default);
            List<Url> found = parser.Detect();

            List<string> allowedTlds = new List<string> { "ai", "com", "co", "net", "org", "io", "info", "xyz", "us", "de", "me", "tv", "dev", "pro", "edu", "be" };

            foreach(Url url in found)
            {
                if(found.Any(url => allowedTlds.Any(tld => url.GetHost().ToString().EndsWith(tld))))
                {
                    List<string> liste = new WebClient().DownloadString($"{Environment.GetEnvironmentVariable("linkwhitelist_url")}").Split('\n').ToList();
                    string formatted_log_msg = message.Content;
                    
                    if (!liste.Contains(url.GetHost().Replace("www.", "")))
                    {
                        await message.DeleteAsync();

                        //log
                        var log_embed = new EmbedBuilder
                        {
                            Author = new EmbedAuthorBuilder().WithName("Message deleted"),
                            Title = $"A message has been deleted in {(message.Channel as SocketTextChannel).Mention}",
                            Description = $"Reason: Not allowed link found\nContent of the deleted message from {message.Author.Mention}:```\n{message.Content.Replace("`", "`​")}```", //caution! here are “zero-width blanks”
                            Color = Color.Red,
                            Footer = new EmbedFooterBuilder().WithText($"Liebo v{version}"),
                        }
                        .Build();

                        await logchannel.SendMessageAsync(embed: log_embed);
                    }
                }
            }
        }

        //ai chat
        if(message.Channel.Id == aichannel.Id && message.Content.Contains(_client.CurrentUser.Mention) && !message.Author.IsBot)
        {
            try{
                await message.Channel.TriggerTypingAsync(); //typing indicator

                List<ChatMessage> previous_messages = new List<ChatMessage>
                {
                    new SystemChatMessage(@"You are Liebo, a Discord Bot on the official Discord server of “LibreChat”. LibreChat is a free, open source AI chat platform. On LibreChat, all users can use all kinds of artificial intelligence with their own API keys.
Your job is to help users with questions about LibreChat. You are only allowed to answer questions about LibreChat, not about any other topic.
You will always answer in a friendly manner and be specific to the question asked.
You cannot see message histories, you can only reply to the question that has just been asked.
The official website of Librechat is “https://www.librechat.ai/”, the official guide is “https://www.librechat.ai/docs”. There is a public demo, which can be accessed at “https://librechat-librechat.hf.space/”. The source code is available on GitHub at “https://github.com/danny-avila/LibreChat”.
Anyone can also download LibreChat and host it on their own system.
You always answer with Markdown and if you don't know something, you refer the users to the LibreChat documentation: “https://www.librechat.ai/docs”. 
For EVERY question you get, use the “GetDocs” tool to get up-to-date information about LibreChat. Make sure you choose the right file! You never answer from your own knowledge, you always use the tools!
You always adhere to these guidelines and never deviate from them!
Note that you do not know everything about LibreChat and your tips may not always work. If necessary, point this out to the user."),
                    new AssistantChatMessage("How can i help you with Librechat?"),
                    new UserChatMessage(message.Content.Replace(_client.CurrentUser.Mention, "").Replace("\"", "\\\"").ReplaceLineEndings(" "))
                };

                OpenAIClientOptions settings = new()
                {
                    Endpoint = new Uri("https://api.groq.com/openai/v1"),
                };
                
                ChatClient oa_client = new(model: "llama-3.2-90b-vision-preview", new ApiKeyCredential(Environment.GetEnvironmentVariable("groq_apikey")), options: settings);

                ChatCompletionOptions options = new()
                {
                    Tools = { getDocs_tool },
                    Temperature = 0.2f,
                };

                ChatCompletion chatCompletion = await oa_client.CompleteChatAsync(previous_messages, options);

                //tool
                bool requiresAction;

                do
                {
                    requiresAction = false;
                    chatCompletion = oa_client.CompleteChat(previous_messages, options);

                    switch (chatCompletion.FinishReason)
                    {
                        case ChatFinishReason.Stop:
                            {
                                break;
                            }

                        case ChatFinishReason.ToolCalls:
                            {
                                //first, add the assistant message with tool calls to the conversation history.
                                previous_messages.Add(new AssistantChatMessage(chatCompletion));

                                //then, add a new tool message for each tool call that is resolved.
                                foreach (ChatToolCall toolCall in chatCompletion.ToolCalls)
                                {
                                    switch (toolCall.FunctionName)
                                    {
                                        case nameof(GetDocs):
                                            {
                                                string toolResult = GetDocs(Regex.Match(toolCall.FunctionArguments.ToString(), @"(?<=""filename"":\s*\"")(.*?)(?=\"")").Value);
                                                previous_messages.Add(new ToolChatMessage(toolCall.Id, toolResult.ToString()));
                                                break;
                                            }

                                        default:
                                            {
                                                throw new NotImplementedException();
                                            }
                                    }
                                }

                                requiresAction = true;
                                break;
                            }

                        default:
                            throw new NotImplementedException(chatCompletion.FinishReason.ToString());
                    }
                } while (requiresAction);

                //modify response
                string response = chatCompletion.Content[0].Text;
                response = response.Replace("Liebo", _client.CurrentUser.Mention);
                response = response.Replace("),", ") ,").Replace(").", ") ."); //fix broken markdown

                await message.Channel.SendMessageAsync($"{response}\n-# This text is AI generated. It may contain mistakes. (Output: {chatCompletion.Usage.OutputTokenCount} Token)", messageReference: new MessageReference(message.Id));
            }
            catch (Exception ex)
            {
                //log
                var log_embed = new EmbedBuilder
                {
                    Title = $"AI Chat Error:",
                    Description = $"Error:\n```{ex.Message}```",
                    Color = Color.Red,
                }
                .Build();
                await logchannel.SendMessageAsync(embed: log_embed);

                var error_response_embed = new EmbedBuilder
                {
                    Description = $"### *Unfortunately, an error has occurred.*\nI can't answer your question right now.\nThe error has been logged and a solution is already being worked on.\n*Thank you for your understanding!*",
                    Footer = new EmbedFooterBuilder().WithText($"Liebo v{version}"),
                }
                .Build();
                await message.Channel.SendMessageAsync(embed: error_response_embed, messageReference: new MessageReference(message.Id));
            }
        }
    }

    //AI Tools ---
    private static readonly ChatTool getDocs_tool = ChatTool.CreateFunctionTool(
        functionName: nameof(GetDocs),
        functionDescription: "Retrieve information from the LibreChats documentation",
        functionParameters: BinaryData.FromBytes("""
        {
            "type": "object",
            "properties": {
                "filename": {
                    "type": "string",
                    "enum": [ "about_librechat_yaml.txt", "anthropic_endpoint.txt", "assistants.txt", "bedrock_aws_endpoint.txt", "code_interpreter.txt", "config_librechat_yaml.txt", "custom_endpoints.txt", "docker_installation.txt", "docker_setup.txt", "env_file.txt", "google_endpoint.txt", "huggingface.txt", "librechat_features_and_functions.txt", "meilisearch.txt", "npm_installation.txt", "ollama.txt", "openai_endpoint.txt", "rag_api.txt", "sst_tts_speech_to_text.txt", "token_usage.txt" ],
                    "description": "The file name of the documentation to be retrieved"
                }
            },
            "required": [ "filename" ]
        }
        """u8.ToArray())
    );
    
    private static string GetDocs(string filename)
    {
        //get docs
        HttpClient client = new HttpClient();
        return client.GetStringAsync($"{Environment.GetEnvironmentVariable("docs_url")}/{filename}{Environment.GetEnvironmentVariable("docs_url_query")}").Result; //access to a selection of librechat docs, which are hosted on a dedicated server. for security reasons here as env
    }
    //AI Tools ---

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
