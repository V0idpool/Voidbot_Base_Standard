using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Voidbot_Discord_Bot_GUI;
using Color = Discord.Color;
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//                                                                                                                                                                                              ||
//                                                                                       Void Mail (Mod Mail) Discord Bot                                                                       ||                
//                                                                                                                                                                                              ||
//                                                       Version: 1.0.0 (Public Release)                                                                                                        ||
//                                                       Author: VoidBot Development Team (Voidpool)                                                                                            ||
//                                                       Release Date: [10/13/2024]                                                                                                             ||
//                                                                                                                                                                                              ||
//                                                       Description:                                                                                                                           ||
//                                                       Void Mail is a highly customizable Discord bot for managing mod mail systems, where users can message staff directly                   ||
//                                                       via DMs, and staff can respond in designated mod mail channels within the server.                                                      ||
//                                                       This release focuses on seamless communication, with MongoDB integration for storing mod mail logs.                                    ||
//                                                                                                                                                                                              ||
//                                                       Current Features:                                                                                                                      ||
//                                                       - Mod mail system allowing users to message staff and receive replies from staff via DM                                                ||
//                                                       - Automatic logging of mod mail conversations in dedicated channel                                                                     ||
//                                                       - Slash commands for setup and configuration                                                                                           ||
//                                                                                                                                                                                              ||
//                                                       Future Features/Ideas (subject to change:                                                                                              ||
//                                                       - Customizable embed messages for mod mail notifications                                                                               ||
//                                                       - Additional moderation tools and logs                                                                                                 ||
//                                                       - Improved user interaction through advanced slash command handling                                                                    ||
//                                                                                                                                                                                              ||
//                                                       Notes:                                                                                                                                 ||
//                                                       - This public release offers a complete mod mail system with background task handling and MongoDB integration.                         ||
//                                                       - Planned updates include further threading optimizations and additional feature integrations.                                         ||
//                                                       - Contributions, donations (BuyMeACoffee & Subscriptions) and suggestions from the community are welcome to shape future releases.     ||
//                                                       - Join our Void Bot Discord Support Server for support, coding help, and updates: https://discord.gg/nsSpGJ5saD                        ||
//                                                                                                                                                                                              ||
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace VoidMail
{
    public class MainProgram
    {

        // MongoDB Connection String & Name (read from UserCFG.ini)
        private string _connectionString;
        private string _databaseName;
        private static InteractionService _interactionService;
        // Flag to check if the bot is in the process of disconnecting
        private static bool isDisconnecting = false;
        private static SemaphoreSlim disconnectSemaphore = new SemaphoreSlim(1, 1);
        // Store the ID of the ephemeral message globally so it can be modified
        private static ulong? _currentEphemeralMessageId;
        public bool IsMongoDBInitialized { get; set; } = false;
        //Define MongoDB Database
        private static IMongoDatabase _database;
        // Define MongoDB Collections here
        public IMongoCollection<ServerSettings> _serverSettingsCollection;
        public IMongoCollection<VoidMailer> _modmailCollection;
        // Form1 instance access
        private static Form1 _instance;

        // Here if needed to refer to the server ID
        private ulong _serverId;
        public string userfile = @"\UserCFG.ini";
        [BsonIgnoreExtraElements]
        public class ServerSettings
        {
            [BsonId]
            public ObjectId Id { get; set; }
            public ulong ServerId { get; set; }
            public bool SetupNotificationSent { get; set; } = false;
            public ulong VoidMailChannelId { get; set; }
        }
        public class VoidMailer
        {
            public ObjectId Id { get; set; }
            public string ServerId { get; set; }
            public ulong UserId { get; set; }
            public string Message { get; set; }
            public DateTime Date { get; set; }
        }
        public static Form1 Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Form1();
                }
                return _instance;
            }
        }

        public MainProgram()
        {
            // File setup and other initialization
            string userFile = @"\UserCFG.ini";
            string userConfigs = Application.StartupPath + userFile;

            if (!System.IO.File.Exists(userConfigs))
            {
                Console.WriteLine("UserCFG.ini not found in Application Directory, Creating file...");
                Module1.SaveToDisk("UserCFG.ini", Application.StartupPath + @"\UserCFG.ini");
            }

            // Load user settings
            _connectionString = UserSettings(Application.StartupPath + userFile, "MongoClientLink");
            _databaseName = UserSettings(Application.StartupPath + userFile, "MongoDBName");
        }
        public async Task<bool> InitializeMongoDBAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(_databaseName))
            {
                Console.WriteLine("MongoDB connection string or database name is missing. Please provide them before running the bot.");
                return false;
            }

            try
            {
                // Initialize MongoDB client and database
                var client = new MongoClient(_connectionString);
                _database = client.GetDatabase(_databaseName);
                // Get collections
                _serverSettingsCollection = _database.GetCollection<ServerSettings>("serverSettings");
                _modmailCollection = _database.GetCollection<VoidMailer>("voidmail");
                Console.WriteLine("MongoDB client initialized successfully.");
                // Mark MongoDB as initialized
                IsMongoDBInitialized = true;
                return true;
            }
            catch (MongoConfigurationException ex)
            {
                Console.WriteLine($"Error initializing MongoDB connection: {ex.Message}");
                return false;
            }
        }
        public async Task<List<RestBan>> GetBanList(ulong guildId)
        {

            var guild = _client.GetGuild(guildId);

            if (guild != null)
            {
                var bansCollections = await guild.GetBansAsync().ToListAsync();

                var bans = bansCollections.SelectMany(collection => collection).ToList();

                return bans;
            }
            else
            {
                Console.WriteLine("[SYSTEM] Server Ban List loading error...");
                return null;
            }
        }
        public async Task PopulateListViewWithConnectedUsersAsync()
        {

            try
            {
                while (true)
                {
                    if (_instance != null && !_instance.IsDisposed && _client != null)
                    {
                        string userfile2 = @"\UserCFG.ini";
                        string GuildIDString = UserSettings(startupPath + userfile2, "ServerID");

                        if (ulong.TryParse(GuildIDString, out ulong GuildID))
                        {
                            var connectedUsers = (await GetConnectedUsersAsync(GuildID)).Cast<SocketUser>().ToList();

                            if (_instance.nsListView1?.IsHandleCreated == true && _instance.nsListView2?.IsHandleCreated == true)
                            {
                                _instance.nsListView1.BeginInvoke(new Action(() =>
                                {
                                    _instance.nsListView1.SuspendLayout();

                                    _instance.nsListView1._Items = _instance.nsListView1._Items
                                        .OrderBy(item => item.Text)
                                        .ToList();

                                    _instance.nsListView1.InvalidateLayout();
                                    _instance.nsListView1.ResumeLayout();
                                }));

                                _instance.nsListView2.BeginInvoke(new Action(() =>
                                {
                                    _instance.nsListView2.SuspendLayout();

                                    List<NSListView.NSListViewItem> itemsToRemove = new List<NSListView.NSListViewItem>();

                                    foreach (var item in _instance.nsListView2._Items.ToList())
                                    {
                                        var userId = ulong.Parse(item.SubItems[1].Text);
                                        if (!connectedUsers.Any(user => user.Id == userId))
                                        {
                                            itemsToRemove.Add(item);
                                        }
                                    }

                                    foreach (var itemToRemove in itemsToRemove)
                                    {
                                        _instance.nsListView2.RemoveItem(itemToRemove);
                                    }

                                    // Sorting nsListView2
                                    connectedUsers.Sort((user1, user2) =>
                                        string.Compare(user1.GlobalName ?? user1.Username, user2.GlobalName ?? user2.Username,
                                            StringComparison.OrdinalIgnoreCase));

                                    foreach (var user in connectedUsers)
                                    {
                                        var nsListViewItem = _instance.nsListView2._Items.FirstOrDefault(item =>
                                            item.Text == (user.GlobalName ?? user.Username));

                                        if (nsListViewItem == null)
                                        {
                                            nsListViewItem = new NSListView.NSListViewItem();
                                            nsListViewItem.Text = user.GlobalName ?? user.Username;
                                            _instance.nsListView2.AddItem(nsListViewItem.Text, user.Username,
                                                user.Id.ToString());
                                        }
                                        else
                                        {
                                            nsListViewItem.SubItems[0].Text = user.Username;
                                            nsListViewItem.SubItems[1].Text = user.Id.ToString();
                                        }


                                    }


                                    _instance.nsListView2.InvalidateLayout();
                                    _instance.nsListView2.ResumeLayout();

                                    _instance.BeginInvoke(new Action(() =>
                                    {
                                        _instance.nsLabel29.Value1 = _instance.nsListView2.Items.Length.ToString();
                                    }));
                                }));

                            }
                        }
                        else
                        {
                            Console.WriteLine("[SYSTEM] Connected users list error... Retrying.");
                        }
                    }

                    // Update Server Members list every 10 seconds to ensure its up to date
                    await Task.Delay(10000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Load Users list: {ex}");
            }

        }





        public async Task PopulateListViewWithBannedUsers()
        {

            // In Task.Run to ensure it does not interfere with the PopulateListViewWithConnectedMembers TODO: Make sure it runs on UI thread properly
            try
            {
                while (true)
                {
                    // Check if the form is still accessible before updating the UI
                    if (_instance != null && !_instance.IsDisposed && _client != null)
                    {
                        string userfile2 = @"\UserCFG.ini";
                        string GuildIDString = UserSettings(startupPath + userfile2, "ServerID");

                        // Convert string to ulong
                        if (ulong.TryParse(GuildIDString, out ulong GuildID))
                        {
                            // Get the ban list for the guild
                            var bans = await GetBanList(GuildID);

                            if (bans != null && _instance.nsListView1?.IsHandleCreated == true)
                            {
                                Task.Run(() =>
                                {
                                    _instance.nsListView1.SuspendLayout();

                                    // Remove users no longer in the ban list
                                    foreach (var item in _instance.nsListView1._Items.ToList())
                                    {
                                        var userId = ulong.Parse(item.SubItems[1].Text);
                                        if (!bans.Any(ban => ban.User.Id == userId))
                                        {
                                            _instance.nsListView1.BeginInvoke(new Action(() =>
                                            {
                                                // Clear all items
                                                _instance.nsListView1.RemoveItemAt(0);
                                            }));
                                        }
                                    }

                                    // Re-add remaining items
                                    _instance.nsListView1.BeginInvoke(new Action(() =>
                                    {
                                        foreach (var ban in bans)
                                        {
                                            var nsListViewItem = _instance.nsListView1._Items.FirstOrDefault(item =>
                                                item.Text == (ban.User.GlobalName ?? ban.User.Username));

                                            if (nsListViewItem == null)
                                            {
                                                nsListViewItem = new NSListView.NSListViewItem();
                                                nsListViewItem.Text = ban.User.GlobalName ?? ban.User.Username;
                                                _instance.nsListView1.AddItem(nsListViewItem.Text, ban.User.Username + " #" + ban.User.Discriminator, ban.User.Id.ToString(), ban.Reason);
                                            }
                                            else
                                            {
                                                nsListViewItem.SubItems[0].Text = ban.User.Username + " #" + ban.User.Discriminator;
                                                nsListViewItem.SubItems[1].Text = ban.User.Id.ToString();
                                                nsListViewItem.SubItems[2].Text = ban.Reason;
                                            }
                                        }

                                        _instance.nsListView1.InvalidateLayout();
                                        _instance.nsListView1.ResumeLayout();

                                        // Update label on the UI thread
                                        _instance.BeginInvoke(new Action(() =>
                                        {
                                            _instance.nsLabel30.Value1 = _instance.nsListView1.Items.Length.ToString();
                                        }));
                                    }));
                                });
                            }
                        }
                        else
                        {
                            Console.WriteLine("[SYSTEM] Server Ban List error... Retrying.");
                        }
                    }

                    // Update Banned Members list every 10 seconds to ensure its up to date
                    await Task.Delay(10000);
                }
            }
            catch
            {
                // Handle exceptions as needed
            }
        }
        // Retrieve the list of connected users
        private async Task<List<SocketGuildUser>> GetConnectedUsersAsync(ulong guildId)
        {
            var guild = DiscordClient.GetGuild(guildId);

            if (guild != null)
            {

                var connectedUsers = guild.Users.ToList();

                return connectedUsers;
            }
            else
            {
                Console.WriteLine("[SYTEM] GetConnectedUsersAsync could not fetch the list...");
                return new List<SocketGuildUser>();
            }
        }

        // Allow access to _client between MainProgram and Form1
        public static DiscordSocketClient _client;
        DiscordSocketClient client = new DiscordSocketClient();
        // Allow access to _mclient between MainProgram and Form1 (If needed, this is here as an example)
        private MongoClient _mclient;

        // Example to handle modal interactions
        private async Task HandleModalInteraction(SocketModal modal)
        {
            var userId = modal.User.Id;
            var serverId = ((SocketGuildChannel)modal.Channel).Guild.Id;

            // Example of how to handle the modal interaction using its custom ID

            //if (modal.Data.CustomId.StartsWith("custom_role_name"))
            //{

            //    var roleName = modal.Data.Components.FirstOrDefault()?.Value;

            //    if (string.IsNullOrWhiteSpace(roleName))
            //    {
            //        await modal.RespondAsync("Role name cannot be empty.", ephemeral: true);
            //        return;
            //    }
            //    if (roleCreationResult == RoleCreationResult.Success)
            //    {
            //        await modal.RespondAsync($"Your custom role '{roleName}' has been created and assigned.", ephemeral: true);
            //    }
            //    else if (roleCreationResult == RoleCreationResult.ReservedRoleName)
            //    {
            //        await modal.RespondAsync("The role name you selected is reserved for staff and cannot be used. Please choose a different name.", ephemeral: true);
            //    }
            //    else
            //    {
            //        await modal.RespondAsync(resultMessage, ephemeral: true);
            //    }
            //}
            //else
            //{
            //    await modal.RespondAsync("Invalid modal submission.", ephemeral: true);
            //}
        }

        // Method to handle button interactions
        private async Task HandleButtonInteraction(SocketMessageComponent component)
        {
            try
            {
                var customId = component.Data.CustomId;
                var serverId = (component.Channel as ITextChannel)?.Guild?.Id ?? 0;


                // Check if it's a reply button
                if (customId.StartsWith("reply_"))
                {
                    // Extract the user ID from the custom ID
                    var userIdString = customId.Split('_')[1];
                    var guildId = customId.Split('_')[2];

                    if (ulong.TryParse(userIdString, out ulong userId) && ulong.TryParse(guildId, out ulong serverID))
                    {
                        // Prompt the support team member to input their reply
                        var modalBuilder = new ModalBuilder()
                            .WithTitle("Reply to Modmail")
                            .WithCustomId($"reply_modal_{userId}_{serverID}")
                            .AddTextInput("Reply", "reply_content", TextInputStyle.Paragraph, "Enter your reply here...", required: true);

                        await component.RespondWithModalAsync(modalBuilder.Build());
                    }
                }
                else
                {
                    // Other button interaction handling (e.g., warn buttons)
                    if (!component.HasResponded)
                    {
                        await component.DeferAsync(ephemeral: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling button interaction: {ex.Message}");
                if (!component.HasResponded)
                {
                    await component.FollowupAsync("An unexpected error occurred. Please try again later.", ephemeral: true);
                }
            }
        }

        public void UpdateMongoDBSettings(string connectionString, string databaseName)
        {
            _connectionString = connectionString;
            _databaseName = databaseName;
        }

        public DiscordSocketClient DiscordClient
        {
            //Access _client from MainProgram and Form1
            get { return _client; }
            private set { _client = value; }
        }
        private string DiscordBotToken;
        string startupPath = AppDomain.CurrentDomain.BaseDirectory;
        string MongoDBConnectionURL;
        string MongoDBName;
        string BotNickname;
        public bool isBotRunning = false;
        private bool shouldReconnect = true;
        // Define an event for log messages
        public event Action<string> LogReceived;
        public event Action<string, string> MessageReception;
        // Define the event
        public event Action<string> BotDisconnected;
        public event Func<Task> BotConnected;
        private CancellationTokenSource cancellationTokenSource;
        // Why? Because I have the dumb.
        static void MainEntryPoint(string[] args)
           => new MainProgram().RunBotAsync().GetAwaiter().GetResult();
        public async Task LoadTasks()
        {
            // Load settings from the INI file (Example, not required)

            string userfile = @"\UserCFG.ini";

            DiscordBotToken = UserSettings(startupPath + userfile, "DiscordBotToken");
            BotNickname = UserSettings(startupPath + userfile, "BotNickname");
            MongoDBConnectionURL = UserSettings(startupPath + userfile, "MongoClientLink");
            MongoDBName = UserSettings(startupPath + userfile, "MongoDBName");
            Console.WriteLine(@"| API Keys Loaded. Opening connection to API Services | Status: Waiting For Connection...");
            // Check if the API keys are properly loaded (Utilize a better method for this please...)
            if (string.IsNullOrEmpty(DiscordBotToken))
            {
                Console.WriteLine("Error: API Error, Settings not configured properly. Are your API Keys correct? Exiting thread.");
                return;
            }
        }
        // User Settings handler
        public string UserSettings(string File, string Identifier)
        {
            using var S = new System.IO.StreamReader(File);
            string Result = "";
            while (S.Peek() != -1)
            {
                string Line = S.ReadLine();
                if (Line.ToLower().StartsWith(Identifier.ToLower() + "="))
                {
                    Result = Line.Substring(Identifier.Length + 1);
                }
            }
            return Result;
        }
        // Access Form1's instance
        public void SetForm1Instance(Form1 form1Instance)
        {
            _instance = form1Instance;
        }
        public async Task StartBotAsync()
        {
            if (!isBotRunning)
            {
                isBotRunning = true;
                // Allow reconnection attempts
                shouldReconnect = true;
                // Start the bot                        
                await RunBotAsync();

            }
        }

        public async Task StopBot()
        {

            if (isBotRunning)
            {

                isBotRunning = false;
                // Prevent reconnection, stop the bot
                shouldReconnect = false;
                await DisconnectBot();

            }
        }

        public async Task DisconnectBot()
        {
            try
            {

                await disconnectSemaphore.WaitAsync();
                // Set flag indicating disconnect is in progress
                isDisconnecting = true;
                if (_client != null && _instance != null)
                {
                    Console.WriteLine("[SYSTEM] Clearing background tasks...");

                    await _client.LogoutAsync();
                    try
                    {
                        // Stop the client
                        await _client.StopAsync();
                        Console.WriteLine("[SYSTEM] Stopping remaining tasks, Waiting...");
                    }
                    catch (Exception logoutException)
                    {
                        Console.WriteLine($"Logout Exception: {logoutException.Message}");
                    }
                    // Dispose of the client
                    _client.Dispose();

                    _client = null;
                    DiscordClient = null;
                    await Task.Delay(2500);
                    _instance.label2.Text = " Not Connected...";
                    _instance.label2.ForeColor = System.Drawing.Color.Red;

                    Console.WriteLine("[SYSTEM] Refreshed client, Client Ready...");

                }
            }
            finally
            {
                isDisconnecting = false;
                disconnectSemaphore.Release();
            }
        }

        private async Task OnClientConnected()
        {
            Console.WriteLine("[SYSTEM] VB Discord Bot connected to Discord...");

            // Initialize MongoDB and check if successful
            IsMongoDBInitialized = await InitializeMongoDBAsync();
            if (!IsMongoDBInitialized)
            {
                Console.WriteLine("MongoDB Connection failed. Please check connection URL and database name.");
                return;
            }
            await RegisterSlashCommands();
            Console.WriteLine("Slash commands registered.");
        }
        public async Task LogModmail(ulong userId, string messageContent, string serverId)
        {
            var modmailLog = new VoidMailer
            {
                UserId = userId,
                Message = messageContent,
                ServerId = serverId,
                Date = DateTime.UtcNow
            };

            await _modmailCollection.InsertOneAsync(modmailLog);
        }
        public async Task HandleModmailDirectMessage(SocketMessage message)
        {
            // Check if the message is from a DM channel
            if (message.Channel is SocketDMChannel)
            {
                // Get all guilds the bot and user share
                var mutualGuilds = _client.Guilds.Where(guild => guild.GetUser(message.Author.Id) != null);

                if (!mutualGuilds.Any())
                {
                    Console.WriteLine($"User {message.Author.Username} is not a member of any mutual guilds.");
                    return;
                }

                foreach (var guild in mutualGuilds)
                {
                    // Retrieve the server settings for this guild from MongoDB
                    var serverSettings = await GetServerSettings(guild.Id);
                    var voidmailChannelId = serverSettings.VoidMailChannelId;

                    if (voidmailChannelId == 0)
                    {
                        Console.WriteLine($"VoidMailChannelId is not set in server settings for guild {guild.Name}.");
                        continue;
                    }

                    var modmailChannel = guild.GetTextChannel(voidmailChannelId);
                    if (modmailChannel == null)
                    {
                        Console.WriteLine($"Modmail channel not found by ID in the guild {guild.Name}.");
                        continue;
                    }

                    var embed = new EmbedBuilder()
                        .WithTitle("📩 New Modmail Received")
                        .WithColor(Color.Orange)
                        .WithDescription($"**Message from {message.Author.Mention}**\n{message.Content}")
                        .WithThumbnailUrl(message.Author.GetAvatarUrl() ?? message.Author.GetDefaultAvatarUrl())
                        .WithTimestamp(DateTime.UtcNow)
                        .WithFooter(footer => footer.Text = $"Modmail received | Guild: {guild.Name}")
                        .Build();

                    var component = new ComponentBuilder()
                        .WithButton("Reply", customId: $"reply_{message.Author.Id}_{guild.Id}", ButtonStyle.Primary)
                        .Build();

                    await modmailChannel.SendMessageAsync(embed: embed, components: component);

                    var userEmbed = new EmbedBuilder()
                        .WithTitle("✅ Modmail Sent")
                        .WithColor(Color.Green)
                        .WithDescription("Your modmail has been sent to the support team. You will be contacted soon.")
                        .WithFooter("Thank you for reaching out!")
                        .WithTimestamp(DateTime.UtcNow)
                        .Build();

                    try
                    {
                        await message.Author.SendMessageAsync(embed: userEmbed);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send DM to user {message.Author.Username}: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine("Message was not from a DM channel. This method only handles modmail through DMs.");
            }
        }


        public async Task HandleModmail(SocketSlashCommand slashCommand)
        {
            if (!slashCommand.HasResponded)
            {
                await slashCommand.DeferAsync(ephemeral: true);
            }

            // Ensure the bot is logged in (Debugging, had issue in beginning)
            if (_client.LoginState != LoginState.LoggedIn)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("❌ Error")
                    .WithColor(Color.Red)
                    .WithDescription("Bot is not fully logged in yet. Please try again later.")
                    .Build();

                await slashCommand.FollowupAsync(embed: embed, ephemeral: true);
                Console.WriteLine("Error: Client is not logged in.");
                return;
            }
            var guildId = ((SocketGuildChannel)slashCommand.Channel).Guild.Id;
            var serverSettings = await GetServerSettings(guildId);
            var voidmailChannelId = serverSettings.VoidMailChannelId;

            // Check if VoidMailChannelId is set
            if (serverSettings.VoidMailChannelId == 0)
            {
                Console.WriteLine("VoidMailChannelId is not set in server settings.");
                return;
            }

            // Fetch the guild and modmail channel using the retrieved VoidMailChannelId
            var guild = _client.GetGuild(guildId);
            if (guild == null)
            {
                Console.WriteLine("Guild not found. Please ensure the bot is a member of the guild.");
                return;
            }

            var modmailChannel = guild.GetTextChannel(voidmailChannelId);

            if (modmailChannel == null)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("❌ Error")
                    .WithColor(Color.Red)
                    .WithDescription("Modmail channel not found. Please contact an admin.")
                    .Build();

                await slashCommand.FollowupAsync(embed: embed, ephemeral: true);
                Console.WriteLine($"Error: Modmail channel with ID {voidmailChannelId} not found in guild {guild?.Name}.");
                return;
            }

            var userMessage = slashCommand.Data.Options.FirstOrDefault()?.Value?.ToString();
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                var embed = new EmbedBuilder()
                    .WithTitle("❌ Error")
                    .WithColor(Color.Red)
                    .WithDescription("Please provide a message for the support team.")
                    .Build();

                await slashCommand.FollowupAsync(embed: embed, ephemeral: true);
                return;
            }

            var modmailEmbed = new EmbedBuilder()
                .WithTitle("📩 New Modmail Received")
                .WithColor(Color.Orange)
                .WithDescription($"**Message from {slashCommand.User.Mention}**\n{userMessage}")
                .WithTimestamp(DateTime.UtcNow)

                .Build();
            var component = new ComponentBuilder()
                .WithButton("Reply", customId: $"reply_{slashCommand.User.Id}_{guildId}", ButtonStyle.Primary)
                .Build();
            // Send the modmail message to the designated modmail channel
            await modmailChannel.SendMessageAsync(embed: modmailEmbed, components: component);

            // Acknowledge the user that their message is received with an embed
            var userEmbed = new EmbedBuilder()
                .WithTitle("✅ Modmail Sent")
                .WithColor(Color.Green)
                .WithDescription("Your modmail has been sent to the support team. You will be contacted soon.")
                .WithFooter("Thank you for reaching out!")

                .Build();

            await slashCommand.FollowupAsync(embed: userEmbed, ephemeral: true);
        }
        // Modal handler for the reply interaction
        private async Task HandleReplyModal(SocketModal modal)
        {
            if (!modal.HasResponded)
            {
                await modal.DeferAsync(ephemeral: true);
            }

            try
            {
                // Extract the user ID and guild ID from the modal's custom ID (Keep track of the right index. I goofed last build)
                var ids = modal.Data.CustomId.Split('_');
                var userIdString = ids[2];  // The user ID
                var guildIdString = ids[3]; // The guild ID

                // Parse user ID and guild ID from the customId
                if (ulong.TryParse(userIdString, out ulong userId) && ulong.TryParse(guildIdString, out ulong guildId))
                {
                    var replyContent = modal.Data.Components.First(x => x.CustomId == "reply_content").Value;

                    // Get the original user and send the reply as a DM
                    var user = _client.GetUser(userId);
                    if (user != null)
                    {
                        var replyEmbed = new EmbedBuilder()
                            .WithTitle("📩 Response from the Support Team")
                            .WithColor(Color.Blue)
                            .WithDescription($"**From:** {modal.User.Mention}\n\n" +
                                             $"**Message:**\n{replyContent}")
                            .WithThumbnailUrl(modal.User.GetAvatarUrl() ?? modal.User.GetDefaultAvatarUrl())
                            .WithTimestamp(DateTime.UtcNow)
                            .WithFooter(footer => footer.Text = "Thank you for reaching out to our support team!")
                            .Build();

                        var componentsDM = new ComponentBuilder()
                            .WithButton("Reply", customId: $"reply_{modal.User.Id}_{guildId}", ButtonStyle.Primary)
                            .Build();

                        try
                        {
                            await user.SendMessageAsync(embed: replyEmbed, components: componentsDM);
                            await modal.FollowupAsync("Reply sent successfully!", ephemeral: true);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to send reply to user: {ex.Message}");
                            await modal.FollowupAsync("Failed to send the reply. The user might have DMs disabled.", ephemeral: true);
                        }

                        // Retrieve the server settings from MongoDB
                        var serverSettings = await GetServerSettings(guildId);
                        var voidmailChannelId = serverSettings.VoidMailChannelId;

                        if (voidmailChannelId == 0)
                        {
                            Console.WriteLine("VoidMailChannelId is not set or found.");
                            return;
                        }

                        var guild = _client.GetGuild(guildId);
                        var modmailChannel = guild?.GetTextChannel(voidmailChannelId);

                        if (modmailChannel != null)
                        {
                            var logEmbed = new EmbedBuilder()
                                .WithTitle("📩 Staff Reply")
                                .WithColor(Color.Green)
                                .WithDescription($"**Reply By:** {modal.User.Mention}\nReply to: {user.Mention}\n\nMessage:\n{replyContent}")
                                .WithThumbnailUrl(modal.User.GetAvatarUrl() ?? modal.User.GetDefaultAvatarUrl())
                                .WithTimestamp(DateTime.UtcNow)
                                .WithFooter("Modmail Reply Logged")
                                .Build();

                            var component = new ComponentBuilder()
                                .WithButton("Reply", customId: $"reply_{userId}_{guildId}", ButtonStyle.Primary)
                                .Build();

                            // Send the reply log to the `voidmail` channel
                            await modmailChannel.SendMessageAsync(embed: logEmbed, components: component);
                        }
                        else
                        {
                            Console.WriteLine("Modmail channel not found by ID.");
                        }
                    }
                    else
                    {
                        await modal.FollowupAsync("User not found!", ephemeral: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling modal interaction: {ex.Message}");
                await modal.FollowupAsync("An error occurred while processing your reply. Please try again later.", ephemeral: true);
            }
        }

        public async Task RunBotAsync()
        {
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Define your Gateway intents, messagecachesize, etc.
                var socketConfig = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences | GatewayIntents.MessageContent,
                    MessageCacheSize = 300
                };
                // Load user settings from config file.
                await LoadTasks();
                _client = new DiscordSocketClient(socketConfig);
                _client.Log += Log;
                _client.MessageReceived += async (message) =>
                {
                    await HandleMessageAsync(message);

                    // Handle modmail messages received via DM
                    if (message.Channel is IDMChannel && !message.Author.IsBot)
                    {
                        // Inform the user to use the `/modmail` command in the relevant server (Makes sure that the DM routes to the correct server it originated from)
                        var responseEmbed = new EmbedBuilder()
                            .WithTitle("❗ Void Mail Request")
                            .WithColor(Color.Blue)
                            .WithDescription("Please use the `/modmail` command in the server where you need assistance. This will ensure your request is routed to the correct support team.")
                            .WithFooter("Thank you for reaching out!")
                            .WithTimestamp(DateTime.UtcNow)
                            .Build();

                        try
                        {
                            await message.Author.SendMessageAsync(embed: responseEmbed);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to send DM to user {message.Author.Username}: {ex.Message}");
                        }
                    }
                };
                _client.Ready += BotConnected;
                _client.InteractionCreated += HandleInteraction;
                _client.SelectMenuExecuted += HandleDropdownSelection;
                _client.InteractionCreated += async (interaction) =>
                {
                    if (interaction is SocketMessageComponent component)
                    {
                        // Handle Warning buttons
                        if (component.Data.CustomId.StartsWith("warn_"))
                        {
                            await HandleButtonInteraction(component);
                        }
                        // Handle Setup select menus
                        else if (component.Data.CustomId.StartsWith("setup_"))
                        {
                            await HandleDropdownSelection(component);
                        }
                        else if (component.Data.CustomId.StartsWith("reply_"))
                        {
                            await HandleButtonInteraction(component);
                        }
                    }
                    else if (interaction is SocketModal modal)
                    {

                        // Handle Reply modal
                        if (modal.Data.CustomId.StartsWith("reply_modal_"))
                        {
                            await HandleReplyModal(modal);
                        }
                        //Examples on how to handle additional modal interactions via its custom ID
                        //else if (modal.Data.CustomId.StartsWith("birthday_event_"))
                        //{
                        //    await HandleBirthdayModal(modal);
                        //}

                    }
                };
                _client.Connected += OnClientConnected;
                _client.Disconnected += async (exception) =>
                {
                    Console.WriteLine($"[SYSTEM] VB Discord Bot disconnected: {exception?.Message}");

                    if (shouldReconnect)
                    {
                        for (int i = 1; i <= 5; i++)  // Retry max 5 times
                        {
                            try
                            {
                                Console.WriteLine($"[SYSTEM] Attempting reconnect #{i}...");
                                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 30)));  // Exponential backoff
                                await StartBotAsync();
                                return;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[SYSTEM] Reconnect attempt #{i} failed: {ex.Message}");
                            }
                        }
                        Console.WriteLine($"[SYSTEM] Failed to reconnect after 5 attempts. Stopping reconnections.");
                    }
                };
                _interactionService = new InteractionService(_client);
                await _client.LoginAsync(TokenType.Bot, DiscordBotToken);
                Console.WriteLine("[SYSTEM] Attempting Login...]");
                await _client.StartAsync();
                Console.WriteLine("[SYSTEM] Logging into Discord Services...");
                await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Bot stopping due to cancellation request.");
                DisconnectBot().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RunBotAsync: {ex.Message}");
            }
        }
        private async Task Log(LogMessage arg)
        {
            // Append the log message to the file
            string logText = $"{DateTime.Now} [{arg.Severity}] {arg.Source}: {arg.Exception?.ToString() ?? arg.Message}";

            // Trigger the event with the log message
            LogReceived?.Invoke(logText);

            // File path to the log file
            string filePath = Path.Combine(startupPath, "bot_logs.txt");

            const int maxRetries = 5;
            const int delayBetweenRetries = 500;
            int retries = 0;

            while (retries < maxRetries)
            {
                try
                {
                    // Use a file lock to ensure only one process writes at a time (Otherwise it will fail at times)
                    using (var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
                    using (var sw = new StreamWriter(fileStream))
                    {
                        await sw.WriteLineAsync(logText);
                    }
                    break;
                }
                catch (IOException)
                {
                    // If the file is in use or cannot be accessed, retry after a delay
                    Console.WriteLine($"Log file is currently in use, retrying... ({retries + 1}/{maxRetries})");
                    retries++;
                    await Task.Delay(delayBetweenRetries);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to log file: {ex.Message}");
                    break;
                }
            }
            // If max retries reached, log a failure message
            if (retries == maxRetries)
            {
                Console.WriteLine("Max retries reached. Failed to write to the log file.");
            }
        }
        // This can be used for prefix based commands, or anything else that requires HandleMessageReceived or HandleInteractionCreated for message listeners. (Used for level systems, logs, prefix commands, etc.)
        public async Task HandleMessageAsync(SocketMessage arg)
        {

            var message = arg as SocketUserMessage;
            if (message == null || message.Author == null || message.Author.IsBot)
            {
                // Either the message is null, the author is null, or the author is a bot, so we ignore it
                return;
            }
            var guildUser = message.Author as SocketGuildUser;
            // Ensure the author is a guild user
            if (guildUser == null) return;

            string userfile = @"\UserCFG.ini";
            string botNickname = UserSettings(startupPath + userfile, "BotNickname");

            var now = DateTime.UtcNow;
            // Load server settings from MongoDB
            var serverSettingsCollection = _database.GetCollection<ServerSettings>("serverSettings");
            var serverSettings = await serverSettingsCollection
                .Find(Builders<ServerSettings>.Filter.Eq(s => s.ServerId, guildUser.Guild.Id))
                .FirstOrDefaultAsync();
        }

        public async Task HandleDropdownSelection(SocketMessageComponent component)
        {
            try
            {
                // Acknowledge the interaction to avoid timeouts
                await component.DeferAsync();

                Console.WriteLine($"Handling dropdown interaction: {component.Data.CustomId}");
                Console.WriteLine($"User selection: {component.Data.Values.FirstOrDefault()}");

                var userSelection = component.Data.Values.FirstOrDefault();
                if (string.IsNullOrEmpty(userSelection))
                {
                    await component.ModifyOriginalResponseAsync(props => props.Content = "No selection made.");
                    return;
                }

                var guildChannel = component.Channel as SocketGuildChannel;
                var guildId = guildChannel?.Guild.Id ?? 0;

                if (guildId == 0)
                {
                    await component.ModifyOriginalResponseAsync(props => props.Content = "Unable to determine the guild ID.");
                    return;
                }

                var serverSettings = await GetServerSettings(guildId);

                if (serverSettings == null)
                {
                    await component.ModifyOriginalResponseAsync(props => props.Content = "Server settings could not be found.");
                    return;
                }

                var responseEmbed = new EmbedBuilder
                {
                    Color = Color.DarkRed,
                    Footer = new EmbedFooterBuilder
                    {
                        Text = "\u200B\n┤|Void Mail (Mod Mail)|├\nhttps://voidbot.lol/",
                        IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                    },
                    Timestamp = DateTime.UtcNow
                };

                // Handle the dropdown selection based on CustomId
                switch (component.Data.CustomId)
                {
                    case string id when id.StartsWith("setup_voidmail_chan"):
                        if (ulong.TryParse(userSelection, out var VoidMailChannelId))
                        {
                            serverSettings.VoidMailChannelId = VoidMailChannelId;
                            await SaveServerSettings(serverSettings);


                            responseEmbed.Title = "✅ Void Mail Staff Channel Set!";
                            responseEmbed.Description = $"The Void Mail Channel has been set to <#{VoidMailChannelId}>.";
                        }
                        else
                        {
                            responseEmbed.Title = "🚫 Error";
                            responseEmbed.Description = "Invalid channel selection.";
                        }
                        break;

                    // Add more settings related stuff here via different cases

                    default:
                        responseEmbed.Title = "🚫 Error";
                        responseEmbed.Description = "Unknown D: It's fine, everything is fine.";
                        break;
                }

                var channel = component.Channel as ITextChannel;

                if (channel == null)
                {
                    await channel.SendMessageAsync("Unable to determine the channel.");
                    return;
                }

                if (_currentEphemeralMessageId.HasValue)
                {
                    var message = await channel.GetMessageAsync(_currentEphemeralMessageId.Value) as IUserMessage;
                    if (message != null)
                    {
                        await message.ModifyAsync(msg => msg.Embed = responseEmbed.Build());
                    }
                    else
                    {
                        // If the message doesn't exist, send a new one
                        var newMessage = await channel.SendMessageAsync(embed: responseEmbed.Build(), allowedMentions: AllowedMentions.None);
                        _currentEphemeralMessageId = newMessage.Id;
                    }
                }
                else
                {
                    // Send a new ephemeral message
                    var newMessage = await channel.SendMessageAsync(embed: responseEmbed.Build(), allowedMentions: AllowedMentions.None);
                    _currentEphemeralMessageId = newMessage.Id;
                }
            }
            catch (Exception ex)
            {
                var channel = component.Channel as ITextChannel;

                if (channel == null)
                {
                    await channel.SendMessageAsync("Unable to determine the channel.");
                    return;
                }
                Console.WriteLine($"Error handling dropdown selection: {ex.Message}");
            }
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            if (interaction is SocketSlashCommand slashCommand)
            {

                if (slashCommand.Data.Name == "setup")
                {
                    try
                    {
                        var guildChannel = slashCommand.Channel as SocketGuildChannel;
                        var guild = guildChannel?.Guild;
                        var user = slashCommand.User as SocketGuildUser;

                        if (guild == null || user == null)
                        {
                            await slashCommand.RespondAsync("Unable to access guild or user information.", ephemeral: true);
                            return;
                        }
                        if (!user.GuildPermissions.Administrator)
                        {
                            await slashCommand.RespondAsync("You do not have permission to use this command.", ephemeral: true);
                            return;
                        }

                        var channels = guild.Channels
                            .OfType<ITextChannel>()
                            .Select(c => new SelectMenuOptionBuilder
                            {
                                Label = c.Name,
                                Value = c.Id.ToString()
                            }).ToList();

                        // Create dropdown with pagination
                        List<SelectMenuBuilder> CreateDropdowns(string customId, string placeholder, IEnumerable<SelectMenuOptionBuilder> options)
                        {
                            var dropdowns = new List<SelectMenuBuilder>();
                            int totalPages = (int)Math.Ceiling((double)options.Count() / 25);

                            for (int i = 0; i < totalPages; i++)
                            {
                                var dropdown = new SelectMenuBuilder()
                                    .WithCustomId($"{customId}_page_{i + 1}")
                                    .WithPlaceholder($"{placeholder} - Page {i + 1} of {totalPages}");

                                foreach (var option in options.Skip(i * 25).Take(25))
                                {
                                    dropdown.AddOption(option.Label, option.Value);
                                }

                                dropdowns.Add(dropdown);
                            }

                            return dropdowns;
                        }

                        var initialEmbed = new EmbedBuilder
                        {
                            Title = "🛠️ Void Mail Basic Setup Wizard",
                            Description = "Welcome to the **Void Mail Setup Wizard**! 🚀\n\n" +
                                          "We'll walk you through the essential configuration for your server.\n\nIt's as easy as setting up a private text channel:\n\n" +
                                          "🔧 **Void Mail Channel**: Select the channel where Void Mail messages will be sent for staff to review and reply.\n" +

                                          "🔍 Use the dropdown menus provided in the following messages to make your selections. Each section will guide you through the process.",
                            ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/settings.png",
                            Color = Color.DarkRed,
                            Footer = new EmbedFooterBuilder
                            {
                                Text = "Let's get started! 🎉",
                                IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                            },
                            Timestamp = DateTime.UtcNow
                        };

                        await slashCommand.RespondAsync(embed: initialEmbed.Build(), ephemeral: true);

                        var embedsAndDropdowns = new List<(EmbedBuilder, List<SelectMenuBuilder>)>
        {
            (
                new EmbedBuilder
                {
                    Title = "Void Mail Channel",
                    Description = "Select the channel where Void Mail messages will be sent for staff to review and reply.",
                    Color = Color.DarkRed,
                    Footer = new EmbedFooterBuilder
                    {
                        Text = "Use the dropdown menu below to select the Void Mail Channel.",
                        IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                    },
                    Timestamp = DateTime.UtcNow
                },
                CreateDropdowns("setup_voidmail_chan", "Select Void Mail Channel...", channels)
            ),

        };

                        var firstEmbedAndDropdowns = embedsAndDropdowns.First();
                        var componentBuilder = new ComponentBuilder();

                        foreach (var dropdown in firstEmbedAndDropdowns.Item2)
                        {
                            var row = new ActionRowBuilder()
                                .AddComponent(dropdown.Build());

                            componentBuilder.AddRow(row);
                        }

                        await slashCommand.FollowupAsync(embed: firstEmbedAndDropdowns.Item1.Build(), components: componentBuilder.Build(), ephemeral: true);

                        for (int i = 1; i < embedsAndDropdowns.Count; i++)
                        {
                            var embedAndDropdowns = embedsAndDropdowns[i];
                            componentBuilder = new ComponentBuilder();

                            foreach (var dropdown in embedAndDropdowns.Item2)
                            {
                                var row = new ActionRowBuilder()
                                    .AddComponent(dropdown.Build());

                                componentBuilder.AddRow(row);
                            }

                            await slashCommand.FollowupAsync(embed: embedAndDropdowns.Item1.Build(), components: componentBuilder.Build(), ephemeral: true);
                        }

                        Console.WriteLine("Setup interaction response sent successfully.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling setup command: {ex.Message}");
                        await slashCommand.RespondAsync("An error occurred while processing your request. Please try again later.", ephemeral: true);
                    }
                }

                else if (slashCommand.Data.Name == "help")
                {
                    var embed = new EmbedBuilder
                    {
                        Title = "📬 **Void Mail Information** 📬",
                        Description = "Welcome to **Void Mail**!\nThis bot handles all mod mail communications, allowing users to message staff directly via DMs, and for staff to reply in the designated mod mail channel.\n\n" +
                                      "ℹ️ **General Information:**\n" +
                                      "Void Mail creates a seamless way for members to contact server staff. All messages sent to the bot via DM will appear in the mod mail channel, where staff can respond privately.\n" +
                                      "🔧 **Basic Commands**:\n" +
                                      "Use the **/setup** command to configure the mod mail channel. ***Ensure only staff members have access to view and reply.***\n\n" +
                                      "For further information or support, please visit our support server: [**Join our Support Server**](https://discord.gg/nsSpGJ5saD)\n",
                        Color = Color.DarkRed,
                        ThumbnailUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                        Footer = new EmbedFooterBuilder
                        {
                            Text = "\u200B\n┤|Void Mail (Mod Mail)|├\nhttps://voidbot.lol/",
                            IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                        }
                    };
                    await slashCommand.RespondAsync(embed: embed.Build());
                }

                else if (slashCommand.Data.Name == "modmail")
                {
                    await HandleModmail(slashCommand);
                }
            }
        }
        // This can be done in a seperate class file, as well as the command handler, This will be done in a later update TODO!!
        private async Task RegisterSlashCommands()
        {
            var commands = new List<SlashCommandBuilder>
    {
        new SlashCommandBuilder()
            .WithName("help")
            .WithDescription("Display information about Void Mail, and its features."),

            new SlashCommandBuilder()
            .WithName("setup")
            .WithDescription("Start the setup wizard for Void Mail.(Admin/Owners Only)"),

            new SlashCommandBuilder()
            .WithName("modmail")
            .WithDescription("Send a mod mail message to the support team.")
            .AddOption("message", ApplicationCommandOptionType.String, "Your message to the support team.", isRequired: true),

            // Add more commands here

        };
            // Build the commands from the builders
            var commandsbuild = commands.Select(builder => builder.Build()).ToArray();

            try
            {
                // Use BulkOverwriteGlobalApplicationCommandsAsync for bulk registration and updates (No longer need to kick the bot to reload commands!)
                await _client.BulkOverwriteGlobalApplicationCommandsAsync(commandsbuild);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering commands: {ex.Message}");
            }
        }

        public async Task<ServerSettings> GetServerSettings(ulong guildId)
        {
            var filter = Builders<ServerSettings>.Filter.Eq(s => s.ServerId, guildId);
            var serverSettings = await _serverSettingsCollection.Find(filter).FirstOrDefaultAsync();

            if (serverSettings == null)
            {
                serverSettings = new ServerSettings
                {
                    Id = ObjectId.GenerateNewId(),
                    ServerId = guildId,
                    VoidMailChannelId = 0,
                    SetupNotificationSent = false
                };

                await _serverSettingsCollection.InsertOneAsync(serverSettings);
            }

            return serverSettings;
        }

        public async Task SaveServerSettings(ServerSettings serverSettings)
        {
            var filter = Builders<ServerSettings>.Filter.Eq(s => s.ServerId, serverSettings.ServerId);

            if (serverSettings.Id == ObjectId.Empty)
            {
                serverSettings.Id = ObjectId.GenerateNewId();
            }

            Console.WriteLine($"Saving ServerSettings: Id={serverSettings.Id}, ServerId={serverSettings.ServerId}");

            var update = Builders<ServerSettings>.Update
             .Set(s => s.VoidMailChannelId, serverSettings.VoidMailChannelId);

            var options = new UpdateOptions { IsUpsert = true };

            await _serverSettingsCollection.UpdateOneAsync(filter, update, options);
        }
    }
}
