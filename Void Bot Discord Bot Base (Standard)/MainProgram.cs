using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Text;
using Color = Discord.Color;
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//                                                                                                                                                                                              ||
//                                                                                 VoidBot Discord Bot                                                                                          ||                
//                                                                                                                                                                                              ||
//                                                       Version: 1.0.0 (Public Release)                                                                                                        ||
//                                                       Author: VoidBot Development Team (Voidpool)                                                                                            ||
//                                                       Release Date: [10/06/2024]                                                                                                             ||
//                                                                                                                                                                                              ||
//                                                       Description:                                                                                                                           ||
//                                                       VoidBot is a highly customizable Discord bot with features such as role management (Mute system Example), XP tracking,                 ||
//                                                       user levels, moderation tools, and custom embeds. This release focuses on a GUI with MongoDB                                           ||
//                                                       integration for user data and server settings management, making it easier to get started.                                             ||
//                                                                                                                                                                                              ||
//                                                       Current Features:                                                                                                                      ||
//                                                       - Verification System (With Autorole given upon successful verification in the verification & welcome channel                          ||
//                                                       - Role management (mute system example)                                                                                                ||
//                                                       - User XP and level tracking, including top user rankings                                                                              ||
//                                                       - Customizable welcome messages and channels                                                                                           ||
//                                                       - Moderation tools such as warnings and bans                                                                                           ||
//                                                       - Lots more, Check out the source! :D                                                                                                  ||
//                                                                                                                                                                                              ||
//                                                       Future Features (More will be added periodically, expect this to change):                                                              ||
//                                                       - Custom embed creation for server admins                                                                                              ||
//                                                       - Advanced moderation and logging features                                                                                             ||
//                                                       - Additional customization for slash commands and interaction handling                                                                 ||
//                                                                                                                                                                                              ||
//                                                       Notes:                                                                                                                                 ||
//                                                       - This public release provides a core set of features for a C# Discord Bot with a GUI and background task handling.                    ||
//                                                       - Planned updates include further threading improvements and additional feature integration.                                           ||
//                                                       - Contributions and suggestions from the community are welcome to shape future releases.                                               ||
//                                                       - Donate or Subscribe to my BuyMeACoffee: https://buymeacoffee.com/voidbot                                                             ||
//                                                       - Join my Void Bot Discord Support Server for support, coding help, and code: https://discord.gg/nsSpGJ5saD                            ||
//                                                                                                                                                                                              ||
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
namespace Voidbot_Discord_Bot_GUI
{
    public class MainProgram
    {
        private Dictionary<string, UserLevelData> userLevels = new Dictionary<string, UserLevelData>();
        private Dictionary<ulong, DateTime> lastMessageTimes = new Dictionary<ulong, DateTime>();
        private const int DEFAULT_XP_COOLDOWN_SECONDS = 60;
        private static int XP_COOLDOWN_SECONDS = DEFAULT_XP_COOLDOWN_SECONDS;
        // MongoDB Connection String & Name (read from UserCFG.ini)
        private string _connectionString;
        private string _databaseName;
        private static InteractionService _interactionService;
        public bool IsMongoDBInitialized { get; set; } = false;
        // Define the maximum number of warnings per page here
        const int warningsPerPage = 5;
        //Define MongoDB Database
        private static IMongoDatabase _database;
        // Define MongoDB Collections here
        public IMongoCollection<Warning> _warningsCollection;
        public IMongoCollection<UserLevelData> _userLevelsCollection;
        private IMongoCollection<MuteInfo> _muteCollection;
        public IMongoCollection<ServerSettings> _serverSettingsCollection;
        // Form1 instance access
        private static Form1 _instance;

        // Here if needed to refer to the server ID
        private ulong _serverId;
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
                _warningsCollection = _database.GetCollection<Warning>("warnings");
                _userLevelsCollection = _database.GetCollection<UserLevelData>("userLevels");
                _muteCollection = _database.GetCollection<MuteInfo>("mutes");
                _serverSettingsCollection = _database.GetCollection<ServerSettings>("serverSettings");
                // Call method to cleanup warnings older than 30 days
                await InitializeWarningCleanup();
                // Ensure proper initialization for verification contexts
                UserContextStore.Initialize(_database);
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
        // Warnings cleanup task
        public async Task InitializeWarningCleanup()
        {
            await SetupWarningCleanup();

        }
        // Warning system below
        public async Task AddWarning(ulong userId, ulong issuerId, ulong serverId, string reason)
        {
            var warning = new Warning
            {
                UserId = userId,
                IssuerId = issuerId,
                ServerId = serverId,
                Reason = reason,
                Date = DateTime.UtcNow
            };

            await _warningsCollection.InsertOneAsync(warning);
        }


        public async Task<List<Warning>> GetWarnings(ulong userId, ulong serverId)
        {
            var filter = Builders<Warning>.Filter.Eq(w => w.UserId, userId) &
                         Builders<Warning>.Filter.Eq(w => w.ServerId, serverId);
            var warnings = await _warningsCollection.Find(filter).ToListAsync();
            return warnings.Where(w => DateTime.UtcNow.Subtract(w.Date).TotalDays <= 30).ToList();
        }


        public async Task<bool> RemoveWarning(ulong userId, ulong serverId, int warningNumber)
        {
            var filter = Builders<Warning>.Filter.Eq(w => w.UserId, userId) &
                         Builders<Warning>.Filter.Eq(w => w.ServerId, serverId);
            var warnings = await _warningsCollection.Find(filter).ToListAsync();
            var warningsToRemove = warnings.Where(w => DateTime.UtcNow.Subtract(w.Date).TotalDays <= 30).ToList();

            if (warningNumber <= warningsToRemove.Count)
            {
                var warningToRemove = warningsToRemove[warningNumber - 1];
                var deleteFilter = Builders<Warning>.Filter.Eq(w => w.Id, warningToRemove.Id);
                await _warningsCollection.DeleteOneAsync(deleteFilter);
                return true;
            }

            return false;
        }


        public async Task ClearWarnings(ulong userId, ulong serverId)
        {
            var filter = Builders<Warning>.Filter.Eq(w => w.UserId, userId) &
                         Builders<Warning>.Filter.Eq(w => w.ServerId, serverId);
            await _warningsCollection.DeleteManyAsync(filter);
        }


        public async Task RemoveOldWarnings()
        {
            if (_warningsCollection == null)
            {
                Console.WriteLine("Warning collection is not initialized.");
                return;
            }
            var filter = Builders<Warning>.Filter.Lt(w => w.Date, DateTime.UtcNow.AddDays(-30));
            await _warningsCollection.DeleteManyAsync(filter);
        }
        // Warning cleanup Task
        public async Task SetupWarningCleanup()
        {
            if (_warningsCollection == null)
            {
                Console.WriteLine("Warning collection is not initialized.");
                return;
            }
            var timer = new System.Threading.Timer(async _ => await RemoveOldWarnings(), null, TimeSpan.Zero, TimeSpan.FromDays(1));
        }
        // Get roles from server to be used in comboboxes
        private Dictionary<string, ulong> newRoles = new Dictionary<string, ulong>();

        public Dictionary<string, ulong> NewRoles
        {
            get { return newRoles; }
        }
        // Allow access to _client between MainProgram and Form1
        public static DiscordSocketClient _client;
        DiscordSocketClient client = new DiscordSocketClient();
        // Allow access to _mclient between MainProgram and Form1 (If needed, this is here as an example)
        private MongoClient _mclient;

        // Method to get the top users based on XP
        private async Task<List<UserLevelData>> GetTopUsers(IMongoDatabase database, ulong serverId, int count)
        {
            if (database == null)
            {
                Console.WriteLine("Database is null.");
                return new List<UserLevelData>();
            }

            var collection = database.GetCollection<UserLevelData>("userLevels");
            if (collection == null)
            {
                Console.WriteLine("Collection is null.");
                return new List<UserLevelData>();
            }

            var filter = Builders<UserLevelData>.Filter.Eq("ServerId", serverId);
            var sort = Builders<UserLevelData>.Sort.Descending("XP");

            try
            {
                var topUsers = await collection.Find(filter)
                                               .Sort(sort)
                                               .Limit(count)
                                               .ToListAsync();

                if (topUsers == null || !topUsers.Any())
                {
                    Console.WriteLine("No top users found.");
                }

                return topUsers;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching top users: {ex.Message}");
                return new List<UserLevelData>();
            }
        }
        // Gets the users name from the top users list
        private async Task<List<KeyValuePair<SocketGuildUser, UserLevelData>>> GetTopUsersWithNamesAsync(IMongoDatabase database, ulong serverId, int count)
        {
            var topUsers = await GetTopUsers(database, serverId, count);

            if (topUsers == null || !topUsers.Any())
            {
                Console.WriteLine("Top users list is null or empty.");
                return new List<KeyValuePair<SocketGuildUser, UserLevelData>>();
            }

            var usersWithNames = new List<KeyValuePair<SocketGuildUser, UserLevelData>>();

            var guild = _client.GetGuild(serverId);
            if (guild == null)
            {
                Console.WriteLine("Guild is null.");
                return new List<KeyValuePair<SocketGuildUser, UserLevelData>>();
            }

            var guildUsersCollection = await guild.GetUsersAsync().FlattenAsync();
            if (guildUsersCollection == null || !guildUsersCollection.Any())
            {
                Console.WriteLine("Guild users collection is null or empty.");
            }

            foreach (var user in topUsers)
            {
                var guildUser = guildUsersCollection.FirstOrDefault(u => u.Id == user.ID) as SocketGuildUser;
                if (guildUser != null)
                {
                    usersWithNames.Add(new KeyValuePair<SocketGuildUser, UserLevelData>(guildUser, user));
                }
                else
                {
                    Console.WriteLine($"User with ID {user.ID} not found in guild.");
                }
            }

            return usersWithNames;
        }



        // Save a single user level to MongoDB
        public async Task SaveUserLevel(UserLevelData userLevel)
        {
            var filter = Builders<UserLevelData>.Filter.Eq(u => u.ID, userLevel.ID) &
                         Builders<UserLevelData>.Filter.Eq(u => u.ServerId, userLevel.ServerId);
            var options = new ReplaceOptions { IsUpsert = true };
            await _userLevelsCollection.ReplaceOneAsync(filter, userLevel, options);
        }

        // Load a single user level from MongoDB
        public async Task<UserLevelData> LoadUserLevel(ulong userId, ulong serverId)
        {
            var filter = Builders<UserLevelData>.Filter.Eq(u => u.ID, userId) &
                         Builders<UserLevelData>.Filter.Eq(u => u.ServerId, serverId);
            return await _userLevelsCollection.Find(filter).FirstOrDefaultAsync();
        }

        // Load all user levels from a specific server
        public async Task<Dictionary<string, UserLevelData>> LoadUserLevels(ulong serverId)
        {
            var filter = Builders<UserLevelData>.Filter.Eq(u => u.ServerId, serverId);
            var userLevelsList = await _userLevelsCollection.Find(filter).ToListAsync();
            return userLevelsList.ToDictionary(u => u.ID.ToString(), u => u);
        }
        // User level data MongoDB Schema
        public class UserLevelData
        {
            [BsonId]
            public ObjectId Id { get; set; }
            public ulong ServerId { get; set; }
            public string Name { get; set; }
            public ulong ID { get; set; }
            public int XP { get; set; }
            public int MessageCount { get; set; }
            // Calculate users level
            public int Level => CalculateLevel();

            [BsonIgnore]
            public int XpForNextLevel => CalculateXpRequiredForLevel(Level + 1);

            public int CalculateLevel()
            {
                return (int)Math.Floor(0.2 * Math.Sqrt(XP));
            }

            public int CalculateXpRequiredForLevel(int level)
            {
                return (int)Math.Pow(level / 0.2, 2);
            }
        }
        // Build the warnings embed
        private Embed BuildWarningsEmbed(List<Warning> warnings, int currentPage, int totalPages, ulong userId)
        {
            var embedBuilder = new EmbedBuilder
            {
                Title = $"⚠️ {_client.GetUser(userId)?.GlobalName ?? "User"}'s Warnings",
                ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/crisis.png",
                Description = $"**‼️ Total Warnings:** {warnings.Count}",
                Color = Color.Orange,
                Footer = new EmbedFooterBuilder
                {
                    Text = $"Page {currentPage}/{totalPages}\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                    IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                },
                Timestamp = DateTime.UtcNow
            };

            var paginatedWarnings = warnings
                .Skip((currentPage - 1) * warningsPerPage)
                .Take(warningsPerPage)
                .ToList();

            foreach (var (warning, index) in paginatedWarnings.Select((w, i) => (w, i)))
            {
                var issuer = _client.GetUser(warning.IssuerId);

                embedBuilder.AddField(
                     "═══════════════════════════",
                    $"📢 **Warning ID: {index + 1 + (currentPage - 1) * warningsPerPage}**",

                    false
                );

                embedBuilder.AddField(
                    "Reason",
                    warning.Reason,
                    false
                );

                embedBuilder.AddField(
                    "Issued By",
                    issuer != null ? $"<@{issuer.Id}>" : "Unknown",
                    true
                );
                embedBuilder.AddField(
                    "Date",
                    $"{warning.Date:MMMM dd, yyyy}",
                    true
                );
                embedBuilder.AddField(
                          "═══════════════════════════",
                          $"\u200B",
                          false
                      );
            }

            return embedBuilder.Build();
        }
        // Method to build the pagination components/buttons
        private MessageComponent BuildPaginationComponents(ulong userId, ulong serverId, int currentPage, int totalPages)
        {
            var buttons = new ComponentBuilder();
            buttons.WithButton("Previous", customId: $"warn_page_{userId}_{serverId}_{currentPage - 1}", ButtonStyle.Secondary, disabled: currentPage == 1);
            buttons.WithButton("Next", customId: $"warn_page_{userId}_{serverId}_{currentPage + 1}", ButtonStyle.Secondary, disabled: currentPage == totalPages);
            return buttons.Build();
        }

        public static async Task UnsetXpForNextLevel()
        {
            var userLevelsCollection = _database.GetCollection<BsonDocument>("userLevels");

            var update = Builders<BsonDocument>.Update.Unset("xpForNextLevel");

            var result = await userLevelsCollection.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty, update);

            Console.WriteLine($"{result.ModifiedCount} documents updated.");
        }


        public async Task<bool> DeductXpPointsAsync(ulong userId, ulong serverId, int amount)
        {
            var userLevel = await LoadUserLevel(userId, serverId);
            if (userLevel == null || userLevel.XP < amount)
            {
                return false;
            }

            userLevel.XP -= amount;
            await SaveUserLevel(userLevel);
            return true;
        }
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



        // Creates or updates the Muted role if it is not found
        private async Task CreateOrUpdateMutedRole(ulong serverId)
        {
            var guild = _client.GetGuild(serverId);
            if (guild == null)
            {
                Console.WriteLine("Guild not found.");
                return;
            }

            try
            {
                // Check if the "Muted" role already exists
                var existingRole = guild.Roles.FirstOrDefault(r => r.Name.Equals("Muted", StringComparison.OrdinalIgnoreCase));
                if (existingRole != null)
                {
                    Console.WriteLine("Muted role already exists.");
                    return;
                }

                // Create a new "Muted" role with default permissions
                var mutedRole = await guild.CreateRoleAsync("Muted", isHoisted: false, isMentionable: false);
                if (mutedRole == null)
                {
                    Console.WriteLine("Failed to create 'Muted' role.");
                    return;
                }

                // Deny sending messages in all text channels
                var channels = guild.TextChannels;
                foreach (var channel in channels)
                {
                    await channel.AddPermissionOverwriteAsync(mutedRole, new OverwritePermissions(sendMessages: PermValue.Deny));
                }

                Console.WriteLine("Created 'Muted' role and configured permissions.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create or configure 'Muted' role: {ex.Message}");
            }
        }

        // Method to handle button interactions
        private async Task HandleButtonInteraction(SocketMessageComponent component)
        {
            try
            {
                var customId = component.Data.CustomId;
                var serverId = (component.Channel as ITextChannel)?.Guild?.Id ?? 0;
                if (!component.HasResponded)
                {
                    await component.DeferAsync(ephemeral: true);
                }
                if (customId.StartsWith("initiate_verify_button_"))
                {

                    var userId = ulong.Parse(customId.Replace("initiate_verify_button_", ""));
                    if (component.User.Id != userId)
                    {
                        await component.FollowupAsync("You cannot verify for another user.", ephemeral: true);
                        return;
                    }
                    var verificationEmbed = new EmbedBuilder
                    {
                        Title = $" Verification Required 🔒",
                        Description = "To gain access to this server, you need to verify your account first.\n\nClick **Verify** below to complete the process, \nor **Support** if you need assistance.",
                        Color = new Color(255, 69, 58),
                        ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/padlock.png",
                        Footer = new EmbedFooterBuilder
                        {
                            Text = "Please complete the verification process to enjoy full access!",
                            IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                        },
                        Timestamp = DateTime.UtcNow
                    };

                    var verifyButton = new ButtonBuilder
                    {
                        Label = "✅ Verify",
                        CustomId = $"final_verify_button_{component.User.Id}",
                        Style = ButtonStyle.Success
                    };

                    var supportButton = new ButtonBuilder
                    {
                        Label = "❓ Support",
                        CustomId = $"support_button_{component.User.Id}",
                        Style = ButtonStyle.Primary
                    };

                    var componentBuilder = new ComponentBuilder()
                        .WithButton(verifyButton)
                        .WithButton(supportButton)
                        .Build();

                    await component.FollowupAsync(embed: verificationEmbed.Build(), components: componentBuilder, ephemeral: true);
                    return;
                }
                if (customId.StartsWith("final_verify_button_"))
                {
                    var userId = ulong.Parse(customId.Replace("final_verify_button_", ""));
                    if (component.User.Id != userId)
                    {
                        await component.FollowupAsync("You cannot verify for another user.", ephemeral: true);
                        return;
                    }

                    var guildUser = component.User as SocketGuildUser;
                    if (guildUser == null)
                    {
                        await component.FollowupAsync("An error occurred: Unable to find the guild user.", ephemeral: true);
                        return;
                    }

                    var serverSettings = await GetServerSettings(serverId);

                    if (serverSettings == null)
                    {
                        await component.FollowupAsync("An error occurred: Server settings not found. Please contact support.", ephemeral: true);
                        return;
                    }
                    // Get the users context from MongoDB
                    var userContext = await UserContextStore.GetAsync(serverId, guildUser.Id);
                    if (userContext != null && userContext.HasVerified)
                    {
                        // If the user has already verified
                        var alreadyVerifiedEmbed = new EmbedBuilder()
                            .WithTitle("❗Already Verified")
                            .WithDescription("You have already verified your account, You can only verify once.\nContact a Staff Member, and they will manually verify you.")
                            .WithColor(new Color(255, 69, 0))
                            .WithThumbnailUrl("https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/refs/heads/main/Img/warning.png")
                            .WithFooter(footer =>
                            {
                                footer.Text = "VoidWatch Security - V1.4";
                                footer.IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png";
                            })
                            .WithTimestamp(DateTime.UtcNow)
                            .Build();

                        await component.FollowupAsync(embed: alreadyVerifiedEmbed, ephemeral: true);
                        return;
                    }
                    if (serverSettings.AutoRoleId != 0)
                    {
                        await guildUser.AddRoleAsync(serverSettings.AutoRoleId);
                        var successEmbed = new EmbedBuilder()
                            .WithTitle($"✅ Verification Successful 🎉")
                            .WithDescription("Congratulations! You have successfully verified your account and now have full access to the server.\n\nEnjoy your stay and make the most of our community!")
                            .WithColor(new Color(0, 204, 102))
                            .WithThumbnailUrl("https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/unlocked.png")
                            .WithFooter(footer =>
                            {
                                footer.Text = "Thank you for verifying!";
                                footer.IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png";
                            })
                            .WithTimestamp(DateTime.UtcNow)
                            .Build();

                        // Update the original verification message
                        await component.FollowupAsync(embed: successEmbed, ephemeral: true);
                        Console.WriteLine("Sent success embed to user.");


                        if (userContext != null)
                        {
                            Console.WriteLine("User context found.");
                            var channel = guildUser.Guild.GetTextChannel(component.Channel.Id);
                            if (channel == null)
                            {
                                Console.WriteLine("Channel not found.");
                                return;
                            }

                            var originalMessage = await channel.GetMessageAsync(userContext.WelcomeMessageId) as IUserMessage;
                            if (originalMessage != null)
                            {
                                Console.WriteLine("Original message found. Modifying message to remove buttons.");
                                await originalMessage.ModifyAsync(msg =>
                                {
                                    var embed = originalMessage.Embeds.FirstOrDefault()?.ToEmbedBuilder().Build();
                                    msg.Embed = embed;
                                    // Remove all buttons
                                    msg.Components = new ComponentBuilder().Build();
                                });

                                // Delete the ping message
                                var pingMessage = await channel.GetMessageAsync(userContext.PingMessageId) as IUserMessage;
                                if (pingMessage != null)
                                {
                                    await pingMessage.DeleteAsync();
                                }
                            }
                            else
                            {
                                Console.WriteLine("Original message not found.");
                            }
                            // Set HasVerified to true in MongoDB, prevents users from spamming verification buttons to bypass muted role restrictions
                            //(This allowed users to reverify after being muted, to be given the Verified auto role)
                            userContext.HasVerified = true;
                            // Update the user context in MongoDB
                            await UserContextStore.AddOrUpdateAsync(userContext);
                        }
                        else
                        {
                            Console.WriteLine("User context not found.");
                        }

                        return;
                    }
                    else
                    {
                        await component.FollowupAsync("An error occurred: AutoRole is not configured. Please contact support.", ephemeral: true);
                        return;
                    }
                }



                if (customId.StartsWith("support_button_"))
                {
                    var userId = ulong.Parse(customId.Replace("support_button_", ""));
                    if (component.User.Id != userId)
                    {
                        await component.FollowupAsync("You cannot request support for another user.", ephemeral: true);
                        return;
                    }

                    var guildUser = component.User as SocketGuildUser;
                    var owner = guildUser?.Guild.Owner;

                    if (owner != null)
                    {
                        var dmChannel = await owner.CreateDMChannelAsync();

                        var embed = new EmbedBuilder()
                            .WithTitle("🔔 Verification Support Request")
                            .WithDescription($"{guildUser.Mention} is having trouble with verification in your server \n**{guildUser.Guild.Name}**.\n\nPlease assist them in verifying their account.")
                            .WithColor(Color.LightOrange)
                            .WithThumbnailUrl("https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/notice.png")
                            .WithTimestamp(DateTimeOffset.Now)
                            .Build();

                        await dmChannel.SendMessageAsync(embed: embed);
                        await component.FollowupAsync("A support request has been sent to the server owner. Please wait for further assistance.", ephemeral: true);
                    }
                    else
                    {
                        await component.FollowupAsync("An error occurred: Unable to contact the server owner. Please try again later.", ephemeral: true);
                    }
                }

                // Handles pagination for the warning embed via Custom Id's
                if (customId.StartsWith("warn_page_"))
                {
                    var components = customId.Split('_');
                    Console.WriteLine($"Custom ID: {customId}, Components: {string.Join(", ", components)}");

                    if (components.Length == 5 && ulong.TryParse(components[2], out var userId) && ulong.TryParse(components[3], out var serverIdga) && int.TryParse(components[4], out var page))
                    {
                        var warnings = await GetWarnings(userId, serverIdga);
                        int totalPages = (int)Math.Ceiling(warnings.Count / (double)warningsPerPage);

                        Console.WriteLine($"User ID: {userId}, Server ID: {serverIdga}, Page: {page}, Total Pages: {totalPages}");

                        if (page < 1 || page > totalPages)
                        {
                            await component.FollowupAsync("Invalid page number.", ephemeral: true);
                            return;
                        }

                        var embed = BuildWarningsEmbed(warnings, page, totalPages, userId);
                        var paginationComponents = BuildPaginationComponents(userId, serverIdga, page, totalPages);

                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Embed = embed;
                            msg.Components = paginationComponents;
                        });

                        return;
                    }
                    else
                    {
                        await component.FollowupAsync("Invalid pagination data.", ephemeral: true);
                        return;
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



        public class Warning
        {
            [BsonId]
            public ObjectId Id { get; set; }
            public ulong UserId { get; set; }
            public ulong ServerId { get; set; }
            public string Reason { get; set; }
            public DateTime Date { get; set; }
            public ulong IssuerId { get; set; }
        }

        public class MuteInfo
        {
            [BsonId]
            public ObjectId Id { get; set; }

            public ulong UserId { get; set; }
            public ulong GuildId { get; set; }
            public ulong RoleId { get; set; }
            public DateTime UnmuteTime { get; set; }
        }
        // Load mutes from MongoDB
        private async Task<List<MuteInfo>> LoadMutesAsync()
        {
            var filter = Builders<MuteInfo>.Filter.Empty;
            var mutes = await _muteCollection.Find(filter).ToListAsync();
            return mutes;
        }

        // Save mute to MongoDB
        private async Task SaveMuteAsync(MuteInfo muteInfo)
        {
            await _muteCollection.InsertOneAsync(muteInfo);
        }

        // Remove mute from MongoDB
        private async Task RemoveMuteAsync(ObjectId muteId)
        {
            var filter = Builders<MuteInfo>.Filter.Eq("_id", muteId);
            await _muteCollection.DeleteOneAsync(filter);
        }

        // Schedule unmute operation
        private async Task ScheduleUnmute(MuteInfo muteInfo)
        {
            var delay = muteInfo.UnmuteTime - DateTime.UtcNow;
            if (delay.TotalMilliseconds <= 0)
            {
                // If delay is negative, set it to zero to unmute
                delay = TimeSpan.Zero;
            }

            var timer = new System.Timers.Timer(delay.TotalMilliseconds);
            timer.Elapsed += async (sender, e) => await HandleUnmute(muteInfo, timer);
            // Ensure it only runs once
            timer.AutoReset = false;
            timer.Start();
        }
        // Method to unmute the user
        private async Task HandleUnmute(MuteInfo muteInfo, System.Timers.Timer timer)
        {
            timer.Stop();
            timer.Dispose();

            var guild = _client.GetGuild(muteInfo.GuildId);
            var user = guild?.GetUser(muteInfo.UserId);
            var muteRole = guild?.GetRole(muteInfo.RoleId);

            if (user != null && muteRole != null)
            {
                await user.RemoveRoleAsync(muteRole);

            }

            try
            {
                await user.SendMessageAsync($"{user.Mention}, you have been un-muted!");
            }
            catch
            {
                Console.WriteLine("Mute System: Un-mute message failed to send!");
            }
            // Remove mute entry in MongoDB
            await RemoveMuteAsync(muteInfo.Id);
        }

        // Load and schedule mutes
        public async Task LoadAndScheduleMutesAsync()
        {
            if (_muteCollection == null)
            {
                Console.WriteLine("Warning collection is not initialized.");
                return;
            }
            var mutes = await LoadMutesAsync();
            foreach (var mute in mutes)
            {
                await ScheduleUnmute(mute);
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
        private string GptApiKey;
        private string DiscordBotToken;
        string startupPath = AppDomain.CurrentDomain.BaseDirectory;
        string userfile;
        string MongoDBConnectionURL;
        string MongoDBName;
        string contentstr;
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
        static void MainEntryPoint(string[] args)
           => new MainProgram().RunBotAsync().GetAwaiter().GetResult();
        public async Task LoadTasks()
        {
            // Load settings from the INI file

            string userfile = @"\UserCFG.ini";

            DiscordBotToken = UserSettings(startupPath + userfile, "DiscordBotToken");
            contentstr = UserSettings(startupPath + userfile, "BotPersonality");
            BotNickname = UserSettings(startupPath + userfile, "BotNickname");
            MongoDBConnectionURL = UserSettings(startupPath + userfile, "MongoClientLink");
            MongoDBName = UserSettings(startupPath + userfile, "MongoDBName");
            Console.WriteLine(@"| API Keys Loaded. Opening connection to API Services | Status: Waiting For Connection...");
            // Check if the API keys are properly loaded
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
        // Flag to check if the bot is in the process of disconnecting
        private static bool isDisconnecting = false;

        private static SemaphoreSlim disconnectSemaphore = new SemaphoreSlim(1, 1);

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

        public async Task RunBotAsync()
        {
            // Utilize cancellation token
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Define your Gateway intents, messagecachesize, etc.
                var socketConfig = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildPresences | GatewayIntents.MessageContent,
                    MessageCacheSize = 300
                };
                // Load user settings
                await LoadTasks();
                _client = new DiscordSocketClient(socketConfig);
                _client.Log += Log;
                _client.MessageReceived += HandleMessageAsync;
                _client.UserJoined += UserJoined;
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
                        // Handle verification buttons
                        else if (component.Data.CustomId.StartsWith("initiate_verify_button_") ||
                                 component.Data.CustomId.StartsWith("final_verify_button_") ||
                                 component.Data.CustomId.StartsWith("support_button_"))
                        {
                            await HandleButtonInteraction(component);
                        }

                    }
                    else if (interaction is SocketModal modal)
                    {
                        //Examples on how to handle modal interactions by its custom ID
                        //if (modal.Data.CustomId.StartsWith("reply_modal_"))
                        //{
                        //    await HandleTicketReplyModal(modal);
                        //}

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
                   // Call DisconnectBot to fully clear the session and reset
                   await DisconnectBot();

                      if (shouldReconnect)
                      {
                         for (int i = 1; i <= 5; i++)  // Retry max 5 times
                           {
                                try
                               {
                                   Console.WriteLine($"[SYSTEM] Attempting reconnect #{i}...");
                                           await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 30)));  // Exponential backoff
                                           await StartBotAsync();
             return;  // Exit on successful reconnect
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
                    // Use a file lock to ensure only one process writes at a time
                    using (var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
                    using (var sw = new StreamWriter(fileStream))
                    {
                        await sw.WriteLineAsync(logText);
                    }

                    // If successful, break
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

        public async Task PopulateComboBoxWithChannels()
        {
            try
            {
                // Check if the form is still accessible before updating the UI
                if (_instance != null && !_instance.IsDisposed && _client != null)
                {
                    string userfile = @"\UserCFG.ini";
                    string serverIdString = UserSettings(Application.StartupPath + userfile, "ServerID");

                    if (ulong.TryParse(serverIdString, out ulong serverId))
                    {
                        var targetGuild = _client?.Guilds.FirstOrDefault(g => g.Id == serverId);

                        if (targetGuild != null)
                        {
                            var newChannels = new HashSet<string>();

                            var textChannels = targetGuild.TextChannels;

                            foreach (var channel in textChannels)
                            {
                                if (channel != null)
                                {
                                    newChannels.Add($"{channel.Name}");
                                }
                            }

                            if (_instance.nsComboBox1.InvokeRequired)
                            {
                                _instance.nsComboBox1?.BeginInvoke(new Action(() =>
                                {
                                    if (!_instance.IsDisposed && _instance.nsComboBox1 != null && !_instance.nsComboBox1.IsDisposed)
                                    {
                                        _instance.nsComboBox1.Sorted = false;
                                        var currentItems = new HashSet<string>(_instance.nsComboBox1.Items.Cast<string>());
                                        if (!currentItems.SetEquals(newChannels))
                                        {
                                            _instance.nsComboBox1.Items.Clear();
                                            foreach (var channel in newChannels)
                                            {
                                                _instance.nsComboBox1.Items.Add(channel);
                                                _instance.nsComboBox1.Sorted = true;
                                            }
                                        }
                                    }
                                }));
                            }
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("Could not populate channel list." + Environment.NewLine + "This could be a network error, or an issue with Discord Servers");
            }
        }





        public async Task PopulateComboBoxWelcomeSettings()
        {
            string userfile = @"\UserCFG.ini";
            try
            {
                string serverIdString = UserSettings(Application.StartupPath + userfile, "ServerID");

                if (ulong.TryParse(serverIdString, out ulong targetServerId))
                {
                    var targetGuild = _client?.Guilds.FirstOrDefault(g => g.Id == targetServerId);

                    if (targetGuild != null)
                    {
                        var textChannels = targetGuild.TextChannels;

                        var newChannels = new HashSet<string>(textChannels.Select(channel => channel.Name));

                        if (_instance.nsComboBox10.InvokeRequired)
                        {
                            _instance.nsComboBox10?.BeginInvoke(new Action(() =>
                            {
                                if (!_instance.IsDisposed && _instance.nsComboBox10 != null && !_instance.nsComboBox10.IsDisposed)
                                {
                                    _instance.nsComboBox10.Sorted = false;
                                    _instance.nsComboBox10.Items.Clear();

                                    foreach (var channel in newChannels)
                                    {
                                        _instance.nsComboBox10.Items.Add(channel);
                                        _instance.nsComboBox10.SelectedItem = UserSettings(Application.StartupPath + userfile, "VerifyWelcomeChannel");
                                        _instance.nsComboBox10.Sorted = true;
                                    }
                                }
                            }));
                        }

                        if (_instance.nsComboBox11.InvokeRequired)
                        {
                            _instance.nsComboBox11?.BeginInvoke(new Action(() =>
                            {
                                if (!_instance.IsDisposed && _instance.nsComboBox11 != null && !_instance.nsComboBox11.IsDisposed)
                                {
                                    _instance.nsComboBox11.Sorted = false;
                                    _instance.nsComboBox11.Items.Clear();

                                    foreach (var channel in newChannels)
                                    {
                                        _instance.nsComboBox11.Items.Add(channel);
                                        _instance.nsComboBox11.SelectedItem = UserSettings(Application.StartupPath + userfile, "RulesChannel");
                                        _instance.nsComboBox11.Sorted = true;
                                    }
                                }
                            }));
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("Could not populate role list." + Environment.NewLine + "This could be a network error, or an issue with Discord Servers");
            }
        }



        public async Task PopulateComboBoxWithRoles()
        {
            try
            {
                string userfile;
                if (_instance != null && !_instance.IsDisposed && _client != null)
                {
                    userfile = @"\UserCFG.ini";
                    string serverIdString = UserSettings(Application.StartupPath + userfile, "ServerID");

                    if (ulong.TryParse(serverIdString, out ulong serverId))
                    {
                        var targetGuild = _client?.Guilds.FirstOrDefault(g => g.Id == serverId);

                        if (targetGuild != null)
                        {
                            var roles = targetGuild.Roles;

                            foreach (var role in roles)
                            {
                                if (role != null)
                                {
                                    newRoles.Add($"{role.Name}", role.Id);
                                }
                            }
                        }
                    }
                    if (_instance.nsComboBox7.InvokeRequired)
                    {

                        userfile = @"\UserCFG.ini";
                        _instance.nsComboBox7?.BeginInvoke(new Action(() =>
                        {
                            if (!_instance.IsDisposed && _instance.nsComboBox7 != null && !_instance.nsComboBox7.IsDisposed)
                            {
                                _instance.nsComboBox7.Sorted = false;
                                var currentItems = new HashSet<string>(_instance.nsComboBox7.Items.Cast<string>());
                                if (!currentItems.SetEquals(newRoles.Keys))
                                {
                                    _instance.nsComboBox7.Items.Clear();
                                    foreach (var role in newRoles.Keys)
                                    {
                                        _instance.nsComboBox7.Items.Add(role);
                                        _instance.nsComboBox7.SelectedItem = UserSettings(Application.StartupPath + userfile, "AutoRoleName");
                                        _instance.nsComboBox7.Sorted = true;
                                    }
                                }
                            }
                        }));
                    }

                    if (_instance.nsComboBox8.InvokeRequired)
                    {

                        userfile = @"\UserCFG.ini";

                        _instance.nsComboBox8?.BeginInvoke(new Action(() =>
                        {
                            if (!_instance.IsDisposed && _instance.nsComboBox8 != null && !_instance.nsComboBox8.IsDisposed)
                            {
                                _instance.nsComboBox8.Sorted = false;
                                var currentItems = new HashSet<string>(_instance.nsComboBox8.Items.Cast<string>());
                                if (!currentItems.SetEquals(newRoles.Keys))
                                {
                                    _instance.nsComboBox8.Items.Clear();
                                    foreach (var role in newRoles.Keys)
                                    {
                                        _instance.nsComboBox8.Items.Add(role);
                                        _instance.nsComboBox8.SelectedItem = UserSettings(Application.StartupPath + userfile, "ModeratorRoleName");
                                        _instance.nsComboBox8.Sorted = true;
                                    }
                                }
                            }
                        }));
                    }

                    if (_instance.nsComboBox9.InvokeRequired)
                    {

                        userfile = @"\UserCFG.ini";
                        _instance.nsComboBox9?.BeginInvoke(new Action(() =>
                        {
                            if (!_instance.IsDisposed && _instance.nsComboBox9 != null && !_instance.nsComboBox9.IsDisposed)
                            {
                                _instance.nsComboBox9.Sorted = false;
                                var currentItems = new HashSet<string>(_instance.nsComboBox9.Items.Cast<string>());
                                if (!currentItems.SetEquals(newRoles.Keys))
                                {
                                    _instance.nsComboBox9.Items.Clear();
                                    foreach (var role in newRoles.Keys)
                                    {
                                        _instance.nsComboBox9.Items.Add(role);
                                        _instance.nsComboBox9.SelectedItem = UserSettings(Application.StartupPath + userfile, "StreamerRoleName");
                                        _instance.nsComboBox9.Sorted = true;
                                    }
                                }
                            }
                        }));
                    }


                    if (_instance.ServerID.InvokeRequired)
                    {

                        _instance.ServerID?.BeginInvoke(new Action(() =>
                        {
                            if (!_instance.IsDisposed && _instance.ServerID != null && !_instance.ServerID.IsDisposed)
                            {
                                string userfile = @"\UserCFG.ini";
                                var INI2 = new Voidbot_Discord_Bot_GUI.inisettings();
                                INI2.Path = Application.StartupPath + @"\UserCFG.ini";

                                if (string.IsNullOrEmpty(UserSettings(Application.StartupPath + userfile, "ServerID")))
                                {
                                    var firstGuild = _client?.Guilds?.FirstOrDefault();
                                    if (firstGuild != null)
                                    {
                                        _instance.ServerID.Text = firstGuild.Id.ToString();
                                    }
                                    INI2.WriteValue("Settings", "ServerID", _instance.ServerID.Text, INI2.GetPath());

                                }
                                else
                                {
                                    _instance.ServerID.Text = UserSettings(Application.StartupPath + userfile, "ServerID");
                                }

                            }
                        }));
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("Could not populate role list." + Environment.NewLine + "This could be a network error, or an issue with Discord Servers");
            }
        }

        public async Task SendMessageToDiscord(string message)
        {
            await Task.Run(async () =>
            {
                while (true)
                {
                    string channelName = _instance.nsComboBox1.SelectedItem?.ToString();

                    // Call the main method with the obtained channelName
                    await SendMessageToDiscord(message, channelName);
                }
            });
        }

        public async Task SendMessageToDiscord(string message, string channelName)
        {
            await Task.Run(async () =>
            {
                if (_client != null && _client.ConnectionState == ConnectionState.Connected)
                {
                    // Find the server where the channel is located
                    var guild = _client.Guilds.FirstOrDefault(g => g.Channels.Any(c => c.Name == channelName));

                    if (guild != null)
                    {
                        // Get the text channel with the specified name
                        var textChannel = guild.TextChannels.FirstOrDefault(c => c.Name == channelName) as ISocketMessageChannel;

                        if (textChannel != null)
                        {
                            // Send the message
                            await textChannel.SendMessageAsync(message);
                        }
                        else
                        {
                            Console.WriteLine($"Unable to send message to channel name '{channelName}'. Text channel not found.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Unable to send message to channel name '{channelName}'. Guild not found.");
                    }
                }
                else
                {
                    Console.WriteLine($"Unable to send message to channel name '{channelName}'. Bot is not connected.");
                }
            });
        }

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
            // If XP system is disabled, return early and do not track XP (Use this with your dashboard, or set it via a command)
            if (serverSettings == null || !serverSettings.XPSystemEnabled)
            {
                return; // XP system is disabled, so no XP tracking will happen
            }
            // If no server-specific settings, fall back to defaults
            int xpCooldownSeconds = serverSettings?.XPCooldown ?? 60;
            int xpAmount = serverSettings?.XPAmount ?? 10;

            // Cooldown check
            if (lastMessageTimes.TryGetValue(message.Author.Id, out var lastMessageTime))
            {
                if ((now - lastMessageTime).TotalSeconds < xpCooldownSeconds)
                {
                    return; // Do not award XP if within cooldown period
                }
            }

            // Update last message time for the cooldown check
            lastMessageTimes[message.Author.Id] = now;

            // Load or create user level data
            var userLevel = await _userLevelsCollection.FindOneAndUpdateAsync(
                Builders<UserLevelData>.Filter.Where(u => u.ID == message.Author.Id && u.ServerId == guildUser.Guild.Id),
                Builders<UserLevelData>.Update.Inc(u => u.MessageCount, 1),
                new FindOneAndUpdateOptions<UserLevelData, UserLevelData> { IsUpsert = true, ReturnDocument = ReturnDocument.After }
            );

            // Update XP in the database
            userLevel = await _userLevelsCollection.FindOneAndUpdateAsync(
                Builders<UserLevelData>.Filter.Where(u => u.ID == message.Author.Id && u.ServerId == guildUser.Guild.Id),
                Builders<UserLevelData>.Update.Inc(u => u.XP, xpAmount),
                new FindOneAndUpdateOptions<UserLevelData, UserLevelData> { IsUpsert = true, ReturnDocument = ReturnDocument.After }
            );

            // Check if level increased
            int oldLevel = userLevel.Level;
            int newLevel = userLevel.Level;
            // Send a message to the channel the user leveled up in
            if (newLevel > oldLevel)
            {
                await message.Channel.SendMessageAsync($"Congratulations, {message.Author.Mention}! You've reached level {newLevel}!");

            }

        }


        // Store the ID of the ephemeral message globally so it can be modified
        private static ulong? _currentEphemeralMessageId;

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
                        Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                        IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                    },
                    Timestamp = DateTime.UtcNow
                };

                // Handle the dropdown selection based on CustomId
                switch (component.Data.CustomId)
                {
                    case string id when id.StartsWith("setup_welcome_channel"):
                        if (ulong.TryParse(userSelection, out var WelcomeChannelId))
                        {
                            serverSettings.WelcomeChannelId = WelcomeChannelId;
                            await SaveServerSettings(serverSettings);


                            responseEmbed.Title = "✅ Verification Channel Set";
                            responseEmbed.Description = $"The Verification Channel has been set to <#{WelcomeChannelId}>.";
                        }
                        else
                        {
                            responseEmbed.Title = "🚫 Error";
                            responseEmbed.Description = "Invalid channel selection.";
                        }
                        break;

                    case string id when id.StartsWith("setup_rules_channel"):
                        if (ulong.TryParse(userSelection, out var rulesChannelId))
                        {
                            serverSettings.RulesChannelId = rulesChannelId;
                            await SaveServerSettings(serverSettings);
                            responseEmbed.Title = "✅ Rules Channel Set";
                            responseEmbed.Description = $"The rules channel has been set to <#{rulesChannelId}>.";
                        }
                        else
                        {
                            responseEmbed.Title = "🚫 Error";
                            responseEmbed.Description = "Invalid channel selection.";
                        }
                        break;

                    case string id when id.StartsWith("setup_autorole"):
                        if (ulong.TryParse(userSelection, out var autoRoleId))
                        {
                            serverSettings.AutoRoleId = autoRoleId;
                            await SaveServerSettings(serverSettings);
                            responseEmbed.Title = "✅ Auto Role Set";
                            responseEmbed.Description = $"The auto role has been set to <@&{autoRoleId}>.";
                        }
                        else
                        {
                            responseEmbed.Title = "🚫 Error";
                            responseEmbed.Description = "Invalid role selection.";
                        }
                        break;

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


        private async Task DeleteOldMessagesAsync(SocketTextChannel channel, IEnumerable<IMessage> oldMessages)
        {
            // Limit the number of concurrent deletions, avoids rate limit issues
            const int maxConcurrency = 5;
            var semaphore = new SemaphoreSlim(maxConcurrency);

            var tasks = oldMessages.Select(async message =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await channel.DeleteMessageAsync(message);
                    // Delay to handle rate limits 5.5 seconds
                    await Task.Delay(5500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting message: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            if (interaction is SocketSlashCommand slashCommand)
            {
                var verifiedServerId = ((SocketGuildChannel)slashCommand.Channel).Guild.Id;

                if (slashCommand.Data.Name == "level")
                {
                    await slashCommand.DeferAsync();

                    var userId = slashCommand.User.Id;
                    var serverId = ((SocketGuildChannel)slashCommand.Channel).Guild.Id;
                    string username = slashCommand.User.GlobalName ?? slashCommand.User.Username;

                    // Load user level information from MongoDB
                    var userLevel = await LoadUserLevel(userId, serverId);
                    if (userLevel == null)
                    {
                        await slashCommand.FollowupAsync("No Rank information for user.", ephemeral: true);
                        return;
                    }
                    // Fetch the list, 1000 is the amount of people to count in the rank list (rank 1-1000)
                    var topUsers = await GetTopUsers(_database, serverId, 1000);
                    // Rank is 1-based index
                    int rank = topUsers.FindIndex(u => u.ID == userId) + 1;

                    var embed = new EmbedBuilder
                    {
                        Author = new EmbedAuthorBuilder
                        {
                            Name = $" {username}'s Stats ",
                            IconUrl = slashCommand.User.GetAvatarUrl() ?? slashCommand.User.GetDefaultAvatarUrl(),
                        },
                        Color = Color.Gold,
                        Footer = new EmbedFooterBuilder
                        {
                            Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                            IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                        },
                        Timestamp = DateTime.UtcNow,
                        ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/userstats.png",
                        Fields = new List<EmbedFieldBuilder>
        {
            new EmbedFieldBuilder
            {
                Name = "Level",
                Value = userLevel.Level.ToString(),
                IsInline = true,
            },
            new EmbedFieldBuilder
            {
                Name = "XP",
                Value = userLevel.XP.ToString(),
                IsInline = true,
            },
            new EmbedFieldBuilder
            {
                Name = "Messages Sent",
                Value = userLevel.MessageCount.ToString(),
                IsInline = false,
            },
            new EmbedFieldBuilder
            {
                Name = "Rank",
                Value = rank > 0 ? $"#{rank}" : "Unranked",
                IsInline = true,
            },
        },
                    };
                    await slashCommand.FollowupAsync(embed: embed.Build());
                    Console.WriteLine("Level Response sent");
                }

                else if (slashCommand.Data.Name == "setup")
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

                        var roles = guild.Roles
                            .Where(r => !r.IsEveryone)
                            .Select(r => new SelectMenuOptionBuilder
                            {
                                Label = r.Name,
                                Value = r.Id.ToString()
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
                            Title = "🛠️ VoidBot Basic Setup Wizard",
                            Description = "Welcome to the **VoidBot Basic Setup Wizard**! 🚀\n\n" +
                                          "We'll walk you through the essential configuration for your server.\n\n**For FULL configuration, use the [VoidBot Dashboard](https://voidbot.lol)**\n\nPlease follow these steps:\n\n" +
                                          "🔧 **Verification Channel**: Choose the channel where new members will be greeted & required to click verify to gain access\n(Anti-raid/Spam) **Set ALL other channels permissions for @everyone to View channel, and set send messages to OFF.\nSet your Autorole roles permissions to View ALL channels you'd like, and Send Messages to on in the appropriate channels.\nSet your preferred member permissions for the AutoRole ID that is assigned after verification.**\n" +
                                          "📜 **Rules Channel**: Pick the channel where the server rules and information will be displayed.\n" +
                                          "🎖️ **Auto Role**: Select the role that will be automatically assigned to new members upon Verification in the Verification channel.\n\n" +
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
                    Title = "🔧 Verification Channel",
                    Description = "Select the channel where Verification messages & Welcome messages will be sent.",
                    Color = Color.DarkRed,
                    Footer = new EmbedFooterBuilder
                    {
                        Text = "Use the dropdown menu below to select the welcome channel.",
                        IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                    },
                    Timestamp = DateTime.UtcNow
                },
                CreateDropdowns("setup_welcome_channel", "Select Verification Channel", channels)
            ),
            (
                new EmbedBuilder
                {
                    Title = "📜 Rules Channel",
                    Description = "Select the channel where the server rules & info will be posted.",
                    Color = Color.DarkRed,
                    Footer = new EmbedFooterBuilder
                    {
                        Text = "Use the dropdown menu below to select the rules channel.",
                        IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                    },
                    Timestamp = DateTime.UtcNow
                },
                CreateDropdowns("setup_rules_channel", "Select Rules Channel", channels)
            ),
            (
                new EmbedBuilder
                {
                    Title = "🎖️ Auto Role",
                    Description = "Select the role that will be automatically assigned to new members after verification.",
                    Color = Color.DarkRed,
                    Footer = new EmbedFooterBuilder
                    {
                        Text = "Use the dropdown menu below to select the auto role.",
                        IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                    },
                    Timestamp = DateTime.UtcNow
                },
                CreateDropdowns("setup_autorole", "Select Auto Role", roles)
            )
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
                else if (slashCommand.Data.Name == "warn")
                {
                    var author = slashCommand.User as SocketGuildUser;

                    if (author?.GuildPermissions.Administrator == true || author.GuildPermissions.BanMembers == true || author.GuildPermissions.KickMembers)
                    {
                        var userIdOption = slashCommand.Data.Options.FirstOrDefault(o => o.Name == "user");
                        var reasonOption = slashCommand.Data.Options.FirstOrDefault(o => o.Name == "reason");
                        var user = userIdOption?.Value as SocketUser;
                        string reason = reasonOption?.Value?.ToString() ?? string.Empty;

                        if (user == null || string.IsNullOrWhiteSpace(reason))
                        {
                            await slashCommand.RespondAsync("Please provide a valid user and a reason for the warning.", ephemeral: true);
                            return;
                        }

                        ulong serverId = (slashCommand.Channel as SocketGuildChannel)?.Guild.Id ?? 0;
                        if (serverId == 0)
                        {
                            await slashCommand.RespondAsync("Unable to determine the server ID.", ephemeral: true);
                            return;
                        }
                        await AddWarning(user.Id, slashCommand.User.Id, serverId, reason);
                        var userWarnings = await GetWarnings(user.Id, serverId);
                        int totalWarnings = userWarnings.Count;

                        DateTime? lastWarningDate = userWarnings.OrderByDescending(w => w.Date).FirstOrDefault()?.Date;

                        var serverSettings = await GetServerSettings(serverId);
                        // Send ping to the warnpingchannelid after the warning is issued
                        if (serverSettings != null && serverSettings.WarnPingChannelId != 0)
                        {
                            var guild = (slashCommand.Channel as SocketGuildChannel)?.Guild;
                            var warnChannel = guild?.GetTextChannel(serverSettings.WarnPingChannelId);

                            if (warnChannel != null)
                            {
                                var confirmationEmbed = new EmbedBuilder
                                {
                                    Title = $"✅ Warning Issued",
                                    Description = $"User {user.Mention} has been warned for the following:\n**Reason:** {reason}",
                                    Color = Color.Green,
                                    Footer = new EmbedFooterBuilder
                                    {
                                        Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                        IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                                    },
                                    Timestamp = DateTime.UtcNow
                                };

                                await slashCommand.RespondAsync(embed: confirmationEmbed.Build(), ephemeral: true);

                                // Embed to notify roles and the server owner about the warning
                                var notifyEmbed = new EmbedBuilder
                                {
                                    Title = "🚨 Warning Notification",
                                    Color = Color.Red,
                                    Description = $"A warning has been issued by: {slashCommand.User.Mention}",
                                    Fields = new List<EmbedFieldBuilder>
                    {
                        new EmbedFieldBuilder
                        {
                            Name = "Warned User",
                            Value = user.Mention,
                            IsInline = true
                        },
                        new EmbedFieldBuilder
                        {
                            Name = "Reason",
                            Value = reason,
                            IsInline = true
                        },
        new EmbedFieldBuilder
        {
            Name = "\u200B",
            Value = "\u200B",
            IsInline = true
        },
                         new EmbedFieldBuilder
                {
                    Name = "Total Warnings",
                    Value = totalWarnings.ToString(),
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Last Warning Date",
                    Value = lastWarningDate.HasValue ? lastWarningDate.Value.ToString("g") : "No previous warnings",
                    IsInline = true
                }
                    },
                                    Footer = new EmbedFooterBuilder
                                    {
                                        Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                        IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                                    },
                                    Timestamp = DateTime.UtcNow
                                };

                                await warnChannel.SendMessageAsync(embed: notifyEmbed.Build());

                                // Notify roles with permissions and the server owner
                                var rolesWithPermissions = guild.Roles
                                    .Where(r => r.Permissions.BanMembers || r.Permissions.ManageGuild || r.Permissions.KickMembers)
                                    .ToList();
                                var owner = guild.Owner;

                                var rolesMentions = string.Join(", ", rolesWithPermissions.Select(r => r.Mention));
                                if (string.IsNullOrEmpty(rolesMentions))
                                {
                                    rolesMentions = "No roles with appropriate permissions found.";
                                }

                                await warnChannel.SendMessageAsync($"{rolesMentions}, {owner.Mention}");
                            }
                            else
                            {
                                await slashCommand.RespondAsync(
                                    "Warning channel not found or invalid channel ID. Please visit [VoidBot Dashboard](https://voidbot.lol/) to configure the bot.",
                                    ephemeral: true
                                );
                            }
                        }
                        else
                        {
                            await slashCommand.RespondAsync(
                                "Warning channel is not configured for this server. Please visit [VoidBot Dashboard](https://voidbot.lol/) to configure the bot.",
                                ephemeral: true
                            );
                        }
                    }
                    else
                    {
                        await slashCommand.RespondAsync("You do not have the required permissions to issue warnings.", ephemeral: true);
                    }
                }

                else if (slashCommand.Data.Name == "warninfo")
                {
                    var authorBan = slashCommand.User as SocketGuildUser;

                    if (authorBan.GuildPermissions.Administrator || authorBan.GuildPermissions.BanMembers)
                    {
                        var usersIdOption = slashCommand.Data.Options.FirstOrDefault(o => o.Name == "user");
                        ulong usersId = usersIdOption?.Value is SocketUser user ? user.Id : 0;

                        if (usersId == 0)
                        {
                            await slashCommand.RespondAsync("Please provide a valid user.");
                            return;
                        }
                        ulong serverId = (slashCommand.Channel as ITextChannel)?.Guild?.Id ?? 0;

                        var warnings = await GetWarnings(usersId, serverId);
                        if (warnings.Count == 0)
                        {
                            await slashCommand.RespondAsync("This user has no warnings.", ephemeral: true);
                            return;
                        }

                        int totalPages = (int)Math.Ceiling(warnings.Count / (double)warningsPerPage);
                        int currentPage = 1;

                        var embed = BuildWarningsEmbed(warnings, currentPage, totalPages, usersId);
                        var components = BuildPaginationComponents(usersId, serverId, currentPage, totalPages);

                        await slashCommand.RespondAsync(embed: embed, components: components, ephemeral: true);
                    }
                }




                else if (slashCommand.Data.Name == "removexp")
                {
                    var userOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user");
                    var amountOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "amount");
                    var authorBan = slashCommand.User as SocketGuildUser;

                    if (authorBan.GuildPermissions.Administrator || authorBan.GuildPermissions.BanMembers)
                    {
                        if (userOption?.Value is SocketGuildUser user && amountOption?.Value is long amount)
                        {
                            await slashCommand.DeferAsync(ephemeral: true);

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var userId = user.Id;
                                    var serverId = ((SocketGuildChannel)slashCommand.Channel).Guild.Id;

                                    var userLevel = await LoadUserLevel(userId, serverId);
                                    if (userLevel == null)
                                    {
                                        await slashCommand.FollowupAsync("User does not have any XP.", ephemeral: true);
                                        return;
                                    }

                                    int oldLevel = userLevel.Level;
                                    userLevel.XP -= (int)amount;
                                    if (userLevel.XP < 0) userLevel.XP = 0;
                                    await SaveUserLevel(userLevel);

                                    int newLevel = userLevel.Level;
                                    if (newLevel < oldLevel)
                                    {
                                        await slashCommand.FollowupAsync($"{user.Mention}, you have been demoted to level {newLevel}.", ephemeral: true);

                                    }
                                    else
                                    {
                                        await slashCommand.FollowupAsync($"{user.Mention} has lost {amount} XP. Total XP: {userLevel.XP}, Level: {userLevel.Level}", ephemeral: true);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error processing removexp command: {ex.Message}");
                                    await slashCommand.FollowupAsync("An error occurred while processing the removexp command.", ephemeral: true);
                                }
                            });
                        }
                        else
                        {
                            await slashCommand.RespondAsync("Invalid command usage. Please specify a user and an amount of XP.", ephemeral: true);
                        }
                    }
                }
                else if (slashCommand.Data.Name == "giveallxp")
                {
                    var user = slashCommand.User;
                    var isBot = user?.IsBot ?? false;
                    var amountOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "amount");
                    var authorBan = slashCommand.User as SocketGuildUser;

                    if (authorBan != null && (authorBan.GuildPermissions.Administrator || authorBan.GuildPermissions.BanMembers))
                    {
                        if (amountOption?.Value is long amount)
                        {
                            await slashCommand.DeferAsync(ephemeral: false);

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var serverId = ((SocketGuildChannel)slashCommand.Channel).Guild.Id;
                                    var users = new List<SocketGuildUser>();

                                    Console.WriteLine("Fetching users...");
                                    // Collect all users from the guild asynchronously
                                    await foreach (var userBatch in authorBan.Guild.GetUsersAsync())
                                    {
                                        Console.WriteLine($"Processing batch of {userBatch.Count()} users...");
                                        // Filter out bots
                                        users.AddRange(userBatch.OfType<SocketGuildUser>().Where(user => !user.IsBot));
                                    }

                                    Console.WriteLine($"Total users fetched: {users.Count}");
                                    Console.WriteLine("Awarding XP to users...");
                                    // Adjust as needed
                                    int batchSize = 50;
                                    int totalUsers = users.Count;
                                    for (int i = 0; i < totalUsers; i += batchSize)
                                    {
                                        var batch = users.Skip(i).Take(batchSize).ToList();
                                        foreach (var user in batch)
                                        {
                                            var userId = user.Id;
                                            var userLevel = await LoadUserLevel(userId, serverId) ?? new UserLevelData
                                            {
                                                Id = ObjectId.GenerateNewId(),
                                                ID = userId,
                                                ServerId = serverId,
                                                Name = user.Username
                                            };

                                            int oldLevel = userLevel.Level;
                                            userLevel.XP += (int)amount;
                                            await SaveUserLevel(userLevel);

                                            int newLevel = userLevel.Level;
                                            if (newLevel > oldLevel)
                                            {
                                                Console.WriteLine($"Batch processing: Updating roles for user {user.Username} (ID: {userId}) from level {oldLevel} to level {newLevel}...");

                                            }
                                        }
                                        Console.WriteLine($"Processed batch {i / batchSize + 1} of {Math.Ceiling((double)totalUsers / batchSize)}");
                                        // Delay to prevent rate limiting
                                        await Task.Delay(1500);
                                    }

                                    Console.WriteLine("All users processed.");
                                    await slashCommand.FollowupAsync($"All of you get... {amount} XP!");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error processing giveallxp command: {ex.Message}");
                                    await slashCommand.FollowupAsync("An error occurred while processing the giveallxp command.", ephemeral: true);
                                }
                            });
                        }
                        else
                        {
                            await slashCommand.RespondAsync("Invalid command usage. Please specify an amount of XP.", ephemeral: true);
                        }
                    }
                }

                else if (slashCommand.Data.Name == "givexp")
                {
                    var userOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user");
                    var amountOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "amount");
                    var authorBan = slashCommand.User as SocketGuildUser;

                    if (authorBan.GuildPermissions.Administrator || authorBan.GuildPermissions.BanMembers)
                    {
                        if (userOption?.Value is SocketGuildUser user && amountOption?.Value is long amount)
                        {
                            await slashCommand.DeferAsync(ephemeral: true);

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var userId = user.Id;
                                    var serverId = ((SocketGuildChannel)slashCommand.Channel).Guild.Id;

                                    var userLevel = await LoadUserLevel(userId, serverId) ?? new UserLevelData
                                    {
                                        Id = ObjectId.GenerateNewId(),
                                        ID = userId,
                                        ServerId = serverId,
                                        Name = user.Username
                                    };

                                    int oldLevel = userLevel.Level;
                                    userLevel.XP += (int)amount;
                                    await SaveUserLevel(userLevel);
                                    await slashCommand.FollowupAsync($"{user.Mention} has been awarded {amount} XP. Total XP: {userLevel.XP}, Level: {userLevel.Level}", ephemeral: true);
                                    int newLevel = userLevel.Level;

                                    if (newLevel > oldLevel)
                                    {
                                        //do a thing


                                    }
                                    else
                                    {
                                        //do nothing, no role update required
                                        Console.WriteLine("XP Given, No Role Update required.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error processing givexp command: {ex.Message}");
                                    await slashCommand.FollowupAsync("An error occurred while processing the givexp command.", ephemeral: true);
                                }
                            });
                        }
                        else
                        {
                            await slashCommand.RespondAsync("Invalid command usage. Please specify a user and an amount of XP.", ephemeral: true);
                        }
                    }
                }

                else if (slashCommand.Data.Name == "roll")
                {
                    // Generate a random number between 1 and 6

                    var result = new Random().Next(1, 13);
                    var embed = new EmbedBuilder
                    {
                        Title = "🎲  Dice Roll  🎲",
                        Description = $"\n{slashCommand.User.Mention} rolled the dice and got: **{result}**",
                        Color = Color.DarkRed,
                        ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/dice.png",
                        Footer = new EmbedFooterBuilder
                        {
                            Text = "┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                            IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                        },
                    };
                    await slashCommand.RespondAsync(embed: embed.Build());
                }


                else if (slashCommand.Data.Name == "8ball")
                {
                    string[] EightBallResponses = { "Yes", "No", "Maybe", "Ask again later", "What do you think?", "...", "Possibly", "No... I mean yes... Well... Ask again later.", "The answer is unclear... Seriously I double checked.", "I won't answer that.", "Yes... Sorry I wan't really listening", "I could tell you, but then I'd have to ban you", "Maybe... I don't know, could you repeat the question?", "You really think I'm answering THAT?", "Yes No Yes No Yes No.", "Ask yourself the same question in the mirror three times, the answer will become clear.", "Noooope" };
                    Random rand = new Random();

                    var questionOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "question");

                    if (questionOption != null && questionOption.Value is string question)
                    {
                        int randomEightBallMessage = rand.Next(EightBallResponses.Length);
                        string messageToPost = EightBallResponses[randomEightBallMessage];

                        var embed = new EmbedBuilder
                        {
                            Title = "🎱  8-Ball Answer  🎱",
                            Description = $"**Question:** {question}\n\n**Answer:** {messageToPost}",
                            ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/8ball.png",
                            Color = Color.DarkRed,
                            Footer = new EmbedFooterBuilder
                            {
                                Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                            },
                        };

                        await slashCommand.RespondAsync(embed: embed.Build());
                        Console.WriteLine("8ball Response sent");
                    }
                    else
                    {
                        await slashCommand.RespondAsync("```" + "Please ask a question after `/8ball`." + "```");
                    }
                }

                else if (slashCommand.Data.Name == "leaderboard")
                {
                    var serverId = ((SocketGuildChannel)slashCommand.Channel).Guild.Id;
                    // Top 10, change as needed
                    int leaderboardSize = 10;

                    if (_database == null)
                    {
                        await slashCommand.RespondAsync("Database not initialized.");
                        return;
                    }

                    try
                    {
                        var topUsers = await GetTopUsersWithNamesAsync(_database, serverId, leaderboardSize);

                        if (topUsers == null || !topUsers.Any())
                        {
                            await slashCommand.RespondAsync("No top users found.");
                            return;
                        }

                        var embed = new EmbedBuilder
                        {
                            Title = "🔥 Server Leaderboard 🔥",
                            Color = Color.Gold,
                            Footer = new EmbedFooterBuilder
                            {
                                Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                            },
                            Timestamp = DateTime.UtcNow,
                            ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/podium.png",
                        };

                        int rank = 1;
                        var mentions = new StringBuilder();

                        foreach (var user in topUsers)
                        {
                            mentions.AppendLine($"**#{rank}**. {user.Key.Mention}\nLevel: {user.Value.Level} | XP: {user.Value.XP}");
                            rank++;
                        }

                        embed.Description = mentions.ToString();
                        await slashCommand.RespondAsync(embed: embed.Build());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing leaderboard: {ex.Message}");
                        await slashCommand.RespondAsync("An error occurred while processing the leaderboard.");
                    }
                }





                else if (slashCommand.Data.Name == "rank")
                {
                    await slashCommand.DeferAsync();

                    var serverId = ((SocketGuildChannel)slashCommand.Channel).Guild.Id;
                    var targetUser = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user")?.Value as IUser;

                    if (targetUser == null)
                    {
                        await slashCommand.FollowupAsync("Invalid command format. Please use `/rank --user @usermention`.", ephemeral: true);
                        return;
                    }

                    string username = targetUser.GlobalName ?? targetUser.Username;

                    // Load user level information
                    var userLevelInfo = await LoadUserLevel(targetUser.Id, serverId);
                    if (userLevelInfo == null)
                    {
                        await slashCommand.FollowupAsync("No Rank information for user.", ephemeral: true);
                        return;
                    }

                    // Retrieve user settings from MongoDB
                    var userSettings = await _userLevelsCollection.Find(Builders<UserLevelData>.Filter.And(
                        Builders<UserLevelData>.Filter.Eq("ID", targetUser.Id.ToString()),
                        Builders<UserLevelData>.Filter.Eq("ServerId", serverId.ToString())
                    )).FirstOrDefaultAsync();

                    // Get all users and find the rank of the target user (rank 1-1000)
                    var allUsers = await GetTopUsers(_database, serverId, 1000);
                    // Find user’s rank, +1 because index starts at 0
                    int rank = allUsers.FindIndex(u => u.ID == targetUser.Id) + 1;

                    if (rank == 0)
                    {
                        await slashCommand.FollowupAsync($"{username} is not ranked.", ephemeral: true);
                        return;
                    }

                    var embed = new EmbedBuilder
                    {
                        Author = new EmbedAuthorBuilder
                        {
                            Name = $" {username}'s Stats ",
                            IconUrl = targetUser.GetAvatarUrl() ?? targetUser.GetDefaultAvatarUrl(),
                        },
                        Color = Color.Gold,
                        Footer = new EmbedFooterBuilder
                        {
                            Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                            IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                        },
                        Timestamp = DateTime.UtcNow,
                        ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/userstats.png",
                        Fields = new List<EmbedFieldBuilder>
        {
            new EmbedFieldBuilder
            {
                Name = "Level",
                Value = userLevelInfo.Level.ToString(),
                IsInline = true,
            },
            new EmbedFieldBuilder
            {
                Name = "XP",
                Value = userLevelInfo.XP.ToString(),
                IsInline = true,
            },
            new EmbedFieldBuilder
            {
                Name = "Messages Sent",
                Value = userLevelInfo.MessageCount.ToString(),
                IsInline = false,
            },
        },
                    };

                    await slashCommand.FollowupAsync(embed: embed.Build());
                    Console.WriteLine("Rank Response sent");
                }

                else if (slashCommand.Data.Name == "duel")
                {
                    var challengedUser = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user")?.Value as IUser;

                    if (challengedUser != null)
                    {
                        IUser challenger = slashCommand.User;
                        int challengerRoll = RollDice();
                        int challengedRoll = RollDice();
                        IUser winner = (challengerRoll > challengedRoll) ? challenger : challengedUser;

                        var embed = new EmbedBuilder
                        {
                            Title = "⚔️  Duel Results  ⚔️",
                            Description = $"**{MentionUtils.MentionUser(challenger.Id)} challenges {MentionUtils.MentionUser(challengedUser.Id)} to a duel!**",
                            Color = Color.DarkRed,
                            ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/duel.png",
                            Fields = new List<EmbedFieldBuilder>
            {
                new EmbedFieldBuilder
                {
                    Name = $"{challenger.GlobalName ?? challenger.Username}'s Roll",
                    Value = $"**{challengerRoll}**",
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = $"{challengedUser.GlobalName ?? challengedUser.Username}'s Roll",
                    Value = $"**{challengedRoll}**",
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "__**Winner**__",
                    Value = $"{MentionUtils.MentionUser(winner.Id)}",
                    IsInline = false
                }
            },
                            Footer = new EmbedFooterBuilder
                            {
                                Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                            },
                        };

                        await slashCommand.RespondAsync(embed: embed.Build());
                        Console.WriteLine("Duel response sent");
                    }
                    else
                    {
                        await slashCommand.RespondAsync("Please mention a user to challenge to a duel.");
                    }
                }
                else if (slashCommand.Data.Name == "help")
                {
                    var embed = new EmbedBuilder
                    {
                        Title = "🤖 **VoidBot Information** 🤖",
                        Description = "Welcome to **VoidBot**! Here's everything you need to know about the bot and our pricing options.\n\n" +
                        "💰 **Bot Pricing Information:**\n" +
                        "🔹 **$4.99 Monthly**\n" +
                        "🔹 **$49.99 Yearly**\n\n" +
                        "ℹ️ **General Information:**\n" +
                        "VoidBot offers a variety of commands and features to enhance your server experience. Premium plans unlock exclusive commands and priority support.\n" +
                        "🎉 [**Click Here to Invite VoidBot to your Server**](https://discord.com/oauth2/authorize?client_id=1199181623674023938)\n\n" +
                        "⚙️ **Setup VoidBot:**\n" +
                        "Use the **/setup** command to configure autoroles, welcome channels, and rules channels. Customize your server’s experience!\n" +
                        "🛠️ [**Click here to visit the VoidBot Dashboard**](https://voidbot.lol)\n\n" +

                        "Need help?\nJoin our support server: [**Join the Support Server**](https://discord.gg/nsSpGJ5saD)\n" +
                        "Visit our Dashboard to invite VoidBot and Manage your settings:\n[**VoidBot Dashboard**](https://voidbot.lol/)\n\n" +
                        "Feel free to reach out if you have any questions or need assistance! 📨",
                        Color = Color.DarkRed,
                        ThumbnailUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                        Footer = new EmbedFooterBuilder
                        {
                            Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                            IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                        }
                    };
                    await slashCommand.RespondAsync(embed: embed.Build());
                }

                else if (slashCommand.Data.Name == "verify")
                {
                    var user = (SocketGuildUser)slashCommand.User;

                    if (user.GuildPermissions.Administrator || user.GuildPermissions.BanMembers || user.GuildPermissions.KickMembers)
                    {
                        var userOption = (SocketGuildUser)slashCommand.Data.Options.FirstOrDefault()?.Value;

                        if (userOption == null)
                        {
                            await slashCommand.RespondAsync("You must specify a user to verify.", ephemeral: true);
                            return;
                        }

                        var guildUser = userOption as SocketGuildUser;
                        if (guildUser == null)
                        {
                            await slashCommand.RespondAsync("Could not find the specified user.", ephemeral: true);
                            return;
                        }

                        var serverId = slashCommand.GuildId.Value;
                        var serverSettings = await GetServerSettings(serverId);

                        if (serverSettings == null)
                        {
                            await slashCommand.RespondAsync("Server settings not found. Please contact support. [VoidBot Support Server](https://discord.gg/nsSpGJ5saD)", ephemeral: true);
                            return;
                        }

                        // Verification Auto role if the server has AutoRoleId configured (this role is given after the user verifies in the verification & welcome channel)
                        if (serverSettings.AutoRoleId != 0)
                        {
                            await guildUser.AddRoleAsync(serverSettings.AutoRoleId);

                            var successEmbed = new EmbedBuilder()
                                .WithTitle("✅ User Verified 🎉")
                                .WithDescription($"{guildUser.Username} has been manually verified and given access to the server.")
                                .WithColor(new Color(0, 204, 102))
                                .WithThumbnailUrl("https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/refs/heads/main/Img/unlocked.png")
                                .WithFooter(footer =>
                                {
                                    footer.Text = "Manual verification complete.";
                                    footer.IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png";
                                })
                                .WithTimestamp(DateTime.UtcNow)
                                .Build();
                            await slashCommand.RespondAsync(embed: successEmbed, ephemeral: true);
                            var guild = _client.GetGuild(serverId);
                            var serverName = guild.Name;
                            // Send DM to the verified user
                            var dmEmbed = new EmbedBuilder()
                                .WithTitle("✅ You Have Been Verified! 🎉")
                                .WithDescription($"Your account has been manually verified by an admin, and you've been granted access to **{serverName}**. Welcome!")
                                .WithColor(new Color(0, 204, 102))
                                .WithThumbnailUrl("https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/refs/heads/main/Img/unlocked.png")
                                .WithFooter(footer =>
                                {
                                    footer.Text = "Verified by the admin team.";
                                    footer.IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png";
                                })
                                .WithTimestamp(DateTime.UtcNow)
                                .Build();

                            try
                            {
                                await guildUser.SendMessageAsync(embed: dmEmbed);
                            }
                            catch (Exception)
                            {
                                await slashCommand.FollowupAsync($"Failed to send DM to {guildUser.Username}. They might have DMs disabled.", ephemeral: true);
                            }

                            return;
                        }
                        else
                        {
                            await slashCommand.RespondAsync("AutoRole is not configured. Please contact support.", ephemeral: true);
                            return;
                        }
                    }
                    else
                    {
                        await slashCommand.RespondAsync("You do not have permission to use this command. This action requires Admin, Kick, or Ban permissions.", ephemeral: true);
                        return;
                    }
                }


                else if (slashCommand.Data.Name == "mute")
                {
                    var userOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user");
                    var durationOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "duration");

                    if (userOption?.Value is SocketGuildUser user && durationOption?.Value is long duration)
                    {
                        var authorMute = slashCommand.User as SocketGuildUser;

                        if (authorMute.GuildPermissions.Administrator || authorMute.GuildPermissions.BanMembers)
                        {
                            var botUser = authorMute.Guild.GetUser(_client.CurrentUser.Id);

                            if (!botUser.GuildPermissions.ManageRoles)
                            {
                                await slashCommand.RespondAsync("I do not have permission to manage roles.", ephemeral: true);
                                return;
                            }
                            var muteRole = authorMute.Guild.Roles.FirstOrDefault(role => role.Name == "Muted");
                            if (muteRole == null)
                            {
                                var restRole = await authorMute.Guild.CreateRoleAsync("Muted", new GuildPermissions(), isMentionable: false);
                                muteRole = authorMute.Guild.Roles.FirstOrDefault(role => role.Id == restRole.Id);

                                // Ensure "Muted" role has correct permissions in all channels
                                foreach (var channel in authorMute.Guild.Channels)
                                {
                                    await channel.AddPermissionOverwriteAsync(muteRole, new OverwritePermissions(sendMessages: PermValue.Deny));
                                }
                            }

                            if (muteRole != null)
                            {
                                if (botUser.Hierarchy <= muteRole.Position)
                                {
                                    await slashCommand.RespondAsync("My role is not high enough to assign the 'Muted' role.", ephemeral: true);
                                    return;
                                }

                                if (botUser.Hierarchy <= user.Hierarchy)
                                {
                                    await slashCommand.RespondAsync("My role is not high enough to mute this user.", ephemeral: true);
                                    return;
                                }

                                await user.AddRoleAsync(muteRole);

                                var unmuteTime = DateTime.UtcNow.AddMinutes(duration);
                                await slashCommand.RespondAsync($"{user.Mention} has been muted for {duration} minutes.", ephemeral: true);

                                var muteInfo = new MuteInfo
                                {
                                    UserId = user.Id,
                                    GuildId = authorMute.Guild.Id,
                                    RoleId = muteRole.Id,
                                    UnmuteTime = unmuteTime
                                };

                                await SaveMuteAsync(muteInfo);
                                await ScheduleUnmute(muteInfo);
                            }
                        }
                        else
                        {
                            await slashCommand.RespondAsync("You don't have permission to use this command.", ephemeral: true);
                        }
                    }
                    else
                    {
                        await slashCommand.RespondAsync("Invalid command usage. Please specify a user and duration.", ephemeral: true);
                    }
                }


                else if (slashCommand.Data.Name == "unmute")
                {
                    var userOption = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user");

                    if (userOption?.Value is SocketGuildUser user)
                    {
                        var authorUnmute = slashCommand.User as SocketGuildUser;

                        if (authorUnmute.GuildPermissions.Administrator || authorUnmute.GuildPermissions.BanMembers)
                        {
                            var botUser = authorUnmute.Guild.GetUser(_client.CurrentUser.Id);

                            if (!botUser.GuildPermissions.ManageRoles)
                            {
                                await slashCommand.RespondAsync("I do not have permission to manage roles.", ephemeral: true);
                                return;
                            }

                            var muteRole = authorUnmute.Guild.Roles.FirstOrDefault(role => role.Name == "Muted");

                            if (muteRole != null)
                            {
                                if (botUser.Hierarchy <= muteRole.Position)
                                {
                                    await slashCommand.RespondAsync("My role is not high enough to remove the 'Muted' role.", ephemeral: true);
                                    return;
                                }

                                await slashCommand.DeferAsync(ephemeral: true);

                                // Perform the role removal and database operation in a separate task
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        // Remove mute entry from MongoDB
                                        var filter = Builders<MuteInfo>.Filter.Eq("UserId", user.Id) & Builders<MuteInfo>.Filter.Eq("GuildId", authorUnmute.Guild.Id);
                                        var result = await _muteCollection.FindOneAndDeleteAsync(filter);

                                        if (result != null)
                                        {
                                            await user.RemoveRoleAsync(muteRole);

                                            await slashCommand.FollowupAsync($"{user.Mention} has been unmuted.");
                                        }
                                        else
                                        {
                                            await slashCommand.FollowupAsync("User was not muted.", ephemeral: true);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error: {ex.Message}");
                                        await slashCommand.FollowupAsync("An error occurred while unmuting the user.", ephemeral: true);
                                    }
                                });
                            }
                            else
                            {
                                await slashCommand.RespondAsync("Muted role not found.", ephemeral: true);
                            }
                        }
                        else
                        {
                            await slashCommand.RespondAsync("You don't have permission to use this command.", ephemeral: true);
                        }
                    }
                    else
                    {
                        await slashCommand.RespondAsync("Invalid command usage. Please specify a user.", ephemeral: true);
                    }
                }
                else if (slashCommand.Data.Name == "say")
                {
                    var user = slashCommand.User as SocketGuildUser;

                    if (user != null && user.GuildPermissions.Administrator)
                    {
                        string messageContent = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "message")?.Value.ToString();

                        var embed = new EmbedBuilder
                        {
                            Description = $"{messageContent}",
                            Color = Color.DarkRed,
                            Footer = new EmbedFooterBuilder
                            {
                                Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                            },
                        };

                        await slashCommand.RespondAsync("\u200B", ephemeral: true);
                        await slashCommand.Channel.SendMessageAsync(embed: embed.Build());
                        Console.WriteLine("Say Command Sent");
                    }
                    else
                    {
                        await slashCommand.RespondAsync("You don't have permission to use this command.", ephemeral: true);
                    }
                }



                else if (slashCommand.Data.Name == "kick")
                {
                    var author = slashCommand.User as SocketGuildUser;

                    if (author.GuildPermissions.KickMembers)
                    {
                        var mention = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user")?.Value as SocketUser;

                        if (mention is SocketGuildUser userToKick)
                        {
                            if (userToKick.IsBot)
                            {
                                await slashCommand.RespondAsync("You cannot kick a bot.");
                                return;
                            }

                            await userToKick.KickAsync();

                            var embed = new EmbedBuilder
                            {
                                Title = "🦵  User Kicked  🦵",
                                Description = $"{author.Mention} kicked {userToKick.GlobalName ?? userToKick.Username} from the server.",
                                Color = Color.Red,

                                Footer = new EmbedFooterBuilder
                                {
                                    Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                    IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                                },
                                Timestamp = DateTime.UtcNow
                            };

                            await slashCommand.RespondAsync(embed: embed.Build());

                            Console.WriteLine($"{author.GlobalName ?? author.Username} kicked {userToKick.GlobalName ?? userToKick.Username}#{userToKick.Discriminator} from the server.");
                        }
                        else
                        {
                            await slashCommand.RespondAsync("Please mention the user you want to kick.");
                        }
                    }
                    else
                    {
                        await slashCommand.RespondAsync("You don't have permission to kick members.", ephemeral: true);
                    }
                }
                else if (slashCommand.Data.Name == "softban")
                {
                    var author = slashCommand.User as SocketGuildUser;

                    if (author.GuildPermissions.Administrator || author.GuildPermissions.KickMembers)
                    {
                        var mention = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user")?.Value as SocketUser;

                        if (mention is SocketGuildUser userToSoftBan)
                        {

                            string reason = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "reason")?.Value as string ?? "No reason specified";
                            await userToSoftBan.Guild.AddBanAsync(userToSoftBan, 1, reason);
                            await userToSoftBan.Guild.RemoveBanAsync(userToSoftBan);

                            var embed = new EmbedBuilder
                            {
                                Title = "🤏  User Softbanned  🤏",
                                Description = $"{author.Mention} softbanned {userToSoftBan.GlobalName ?? userToSoftBan.Username}.",
                                Color = Color.DarkOrange,

                                Fields = new List<EmbedFieldBuilder>
                    {
                        new EmbedFieldBuilder
                        {
                            Name = "Reason",
                            Value = reason,
                            IsInline = false
                        }
                    },

                                Footer = new EmbedFooterBuilder
                                {
                                    Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                    IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                                },
                                Timestamp = DateTime.UtcNow
                            };

                            await slashCommand.RespondAsync(embed: embed.Build());

                            Console.WriteLine($"{author.GlobalName ?? author.Username} softbanned {userToSoftBan.GlobalName ?? userToSoftBan.Username}#{userToSoftBan.Discriminator} from the server. Reason: {reason}");
                        }

                    }
                    else
                    {
                        await slashCommand.RespondAsync("You don't have permission to soft ban members.", ephemeral: true);
                    }
                }

                else if (slashCommand.Data.Name == "ban")
                {
                    var authorBan = slashCommand.User as SocketGuildUser;

                    if (authorBan.GuildPermissions.Administrator || authorBan.GuildPermissions.BanMembers)
                    {
                        var mentionBan = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "user")?.Value as SocketUser;

                        if (mentionBan is SocketGuildUser userToBan)
                        {

                            string reasonBan = slashCommand.Data.Options.FirstOrDefault(option => option.Name == "reason")?.Value as string ?? "No reason specified";
                            await userToBan.BanAsync(reason: reasonBan);

                            var embedBan = new EmbedBuilder
                            {
                                Title = "🔨  BAN Hammer  🔨",
                                Description = $"{authorBan.Mention} banned {userToBan.GlobalName ?? userToBan.Username} from the server.",
                                Color = Color.Red,

                                Fields = new List<EmbedFieldBuilder>
                    {
                        new EmbedFieldBuilder
                        {
                            Name = "Reason",
                            Value = reasonBan,
                            IsInline = false
                        }
                    },
                                Footer = new EmbedFooterBuilder
                                {
                                    Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                    IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                                },
                                Timestamp = DateTime.UtcNow
                            };

                            await slashCommand.RespondAsync(embed: embedBan.Build());

                            Console.WriteLine($"{authorBan.GlobalName ?? authorBan.Username} banned {userToBan.GlobalName ?? userToBan.Username}#{userToBan.Discriminator} from the server. Reason: {reasonBan}");
                        }

                    }
                    else
                    {
                        await slashCommand.RespondAsync("You don't have permission to ban members.", ephemeral: true);
                    }
                }

                else if (slashCommand.Data.Name == "yt")
                {
                    var user = slashCommand.User as SocketGuildUser;
                    string userfile2 = @"\UserCFG.ini";
                    string youtubeAPIKey = UserSettings(startupPath + userfile2, "YoutubeAPIKey");
                    string youtubeappname = UserSettings(startupPath + userfile2, "YoutubeAppName");
                    string query = slashCommand.Data.Options.First().Value.ToString();

                    var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                    {
                        ApiKey = youtubeAPIKey,
                        ApplicationName = youtubeappname
                    });

                    var searchListRequest = youtubeService.Search.List("snippet");
                    searchListRequest.Q = query;
                    searchListRequest.MaxResults = 1;

                    var searchListResponse = await searchListRequest.ExecuteAsync();

                    var searchResult = searchListResponse.Items.FirstOrDefault();

                    if (searchResult != null)
                    {
                        var videoId = searchResult.Id.VideoId;
                        var videoUrl = $"https://www.youtube.com/watch?v={videoId}";
                        var embed = new EmbedBuilder
                        {
                            Author = new EmbedAuthorBuilder
                            {
                                Name = "YouTube Search Result",
                                IconUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/youtubeico.png"
                            },
                            Title = searchResult.Snippet.Title,
                            Url = videoUrl,
                            Description = $"**Description:**\n {searchResult.Snippet.Description}",
                            Color = Color.DarkRed,
                            ThumbnailUrl = searchResult.Snippet.Thumbnails.Default__.Url,
                            Footer = new EmbedFooterBuilder
                            {
                                Text = $"{user.GlobalName ?? user.Username} posted a YouTube search",
                                IconUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
                            },
                            Fields = new List<EmbedFieldBuilder>
        {
            new EmbedFieldBuilder
            {
                Name = "Channel",
                Value = searchResult.Snippet.ChannelTitle

            },
            new EmbedFieldBuilder
            {
                Name = "Video Uploaded On",
                Value = searchResult.Snippet.PublishedAt?.ToString("MM-dd-yyyy HH:mm tt")

            }
        }
                        };

                        await slashCommand.RespondAsync(embed: embed.Build());

                        Console.WriteLine($"{user.Username} posted a YouTube Search: {videoUrl}");
                    }

                    else
                    {
                        await slashCommand.RespondAsync("No search results found.");

                    }
                }

                else if (slashCommand.CommandName == "pm")
                {
                    var isAdmin = (slashCommand.User as IGuildUser)?.GuildPermissions.Administrator ?? false;

                    if (isAdmin)
                    {
                        var mentionedUserOption = slashCommand.Data.Options.FirstOrDefault(o => o.Name == "user");
                        var mentionedUser = mentionedUserOption?.Value as IUser;

                        if (mentionedUser != null)
                        {
                            var messageContentOption = slashCommand.Data.Options.FirstOrDefault(o => o.Name == "message");
                            var messageContent = messageContentOption?.Value?.ToString();

                            try
                            {
                                await mentionedUser.SendMessageAsync(messageContent ?? "No message content provided.");
                                await slashCommand.RespondAsync("PM successfully sent", ephemeral: true);
                                Console.WriteLine("PM Command successfully sent");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("PM Command Error: " + ex.Message);
                                await slashCommand.RespondAsync("Failed to send a private message. User may not allow PMS.", ephemeral: true);
                            }
                        }
                        else
                        {
                            await slashCommand.RespondAsync("Please mention a user to send a PM.", ephemeral: true);
                        }
                    }
                    else
                    {
                        await slashCommand.RespondAsync("You don't have permission to use this command.", ephemeral: true);
                    }
                }

                else if (slashCommand.Data.Name == "live")
                {
                    var user = slashCommand.User as SocketGuildUser;

                    ulong serverId = user.Guild.Id;

                    var serverSettings = await GetServerSettings(serverId);
                    if (serverSettings == null)
                    {
                        await slashCommand.RespondAsync("Server settings not found.", ephemeral: true);
                        return;
                    }
                    ulong streamerRoleId = serverSettings.StreamerRole;
                    var hasStreamerRole = user.Roles.Any(role => role.Id == streamerRoleId);
                    bool isAdmin = user.GuildPermissions.Administrator;
                    bool isOwner = user.Guild.OwnerId == user.Id;
                    bool isModerator = user.GuildPermissions.KickMembers;

                    if (isAdmin || isOwner || isModerator || hasStreamerRole)
                    {
                        var twitchNameOption = slashCommand.Data.Options.FirstOrDefault(o => o.Name == "twitch-name");
                        var gameNameOption = slashCommand.Data.Options.FirstOrDefault(o => o.Name == "game-name");

                        if (twitchNameOption != null && gameNameOption != null)
                        {
                            string twitchName = twitchNameOption.Value.ToString();
                            string gameName = gameNameOption.Value.ToString();

                            DateTime now = DateTime.Now;
                            string displayName = user.DisplayName ?? user.Username;
                            string formattedDateTime = now.ToString("MMMM dd, yyyy" + Environment.NewLine + "'Time:' h:mm tt");

                            var embed = new EmbedBuilder
                            {
                                Title = " ❗ Stream Alert ❗",
                                Color = new Color(0, 255, 0),
                                Description = $"**{displayName}** is now **LIVE** on Twitch\n\n**Playing:**  _{gameName}_",
                                Timestamp = DateTime.UtcNow,
                                Footer = new EmbedFooterBuilder
                                {
                                    Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                                    IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png",
                                },
                                ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/twitchico.png",
                                Author = new EmbedAuthorBuilder
                                {
                                    Name = $"{displayName} Just went LIVE!",
                                    IconUrl = user.GetAvatarUrl()
                                },
                                Fields = new List<EmbedFieldBuilder>
                {
                    new EmbedFieldBuilder
                    {
                        Name = "Started At",
                        Value = formattedDateTime,
                        IsInline = true
                    },
                    new EmbedFieldBuilder
                    {
                        Name = "Watch Now",
                        Value = $"[Click Here to Watch](https://www.twitch.tv/{twitchName})",
                        IsInline = true
                    }
                }
                            };


                            await slashCommand.RespondAsync(embed: embed.Build());
                            Console.WriteLine("Twitch Update sent");
                        }
                        else
                        {
                            await slashCommand.RespondAsync("Please provide a Twitch username and game name. Usage: `/live twitchname gamename`", ephemeral: true);
                        }
                    }
                    else
                    {
                        await slashCommand.RespondAsync("You don't have permission to use this command.", ephemeral: true);
                    }
                }





                else if (slashCommand.Data.Name == "purge")
                {
                    var user = slashCommand.User as SocketGuildUser;

                    var isAdmin = user?.GuildPermissions.Administrator ?? false;
                    var hasSpecificRole = user?.GuildPermissions.ManageMessages ?? false;
                    var isBot = user?.IsBot ?? false;

                    if (isAdmin || hasSpecificRole || isBot)
                    {
                        int messagesToPurge = (int)(long)slashCommand.Data.Options.First().Value;

                        // Optional filters
                        string filterUser = slashCommand.Data.Options.FirstOrDefault(x => x.Name == "user")?.Value as string;
                        string filterKeyword = slashCommand.Data.Options.FirstOrDefault(x => x.Name == "keyword")?.Value as string;
                        bool filterBots = (bool?)(slashCommand.Data.Options.FirstOrDefault(x => x.Name == "bots-only")?.Value) ?? false;

                        var channel = slashCommand.Channel as SocketTextChannel;

                        if (channel != null)
                        {
                            await slashCommand.DeferAsync(ephemeral: true);

                            try
                            {
                                var messages = await channel.GetMessagesAsync(messagesToPurge).FlattenAsync();

                                // Apply optional filters
                                if (!string.IsNullOrEmpty(filterUser))
                                {
                                    messages = messages.Where(m => m.Author.Username.Equals(filterUser, StringComparison.OrdinalIgnoreCase));
                                }

                                if (!string.IsNullOrEmpty(filterKeyword))
                                {
                                    messages = messages.Where(m => m.Content.Contains(filterKeyword, StringComparison.OrdinalIgnoreCase));
                                }

                                if (filterBots)
                                {
                                    messages = messages.Where(m => m.Author.IsBot);
                                }

                                // Separate messages into those older and newer than 14 days
                                var oldMessages = messages.Where(m => (DateTimeOffset.Now - m.CreatedAt).TotalDays >= 14).ToList();
                                var recentMessages = messages.Where(m => (DateTimeOffset.Now - m.CreatedAt).TotalDays < 14).ToList();

                                // Max number of messages to delete per batch
                                const int batchSize = 100;
                                var batches = recentMessages.Batch(batchSize);

                                foreach (var batch in batches)
                                {
                                    try
                                    {
                                        await channel.DeleteMessagesAsync(batch);
                                        // Delay between batches to handle rate limits
                                        await Task.Delay(1000);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error deleting messages: {ex.Message}");
                                    }
                                }

                                // Use DeleteOldMessagesAsync to handle older messages
                                await DeleteOldMessagesAsync(channel, oldMessages);

                                await slashCommand.FollowupAsync($"{user.GlobalName ?? user.Username} Purged {recentMessages.Count()} recent messages and {oldMessages.Count} old messages.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error during purge operation: {ex.Message}");
                                await slashCommand.FollowupAsync("An error occurred while purging messages. Please try again later.", ephemeral: true);
                            }
                        }
                    }
                    else
                    {
                        await slashCommand.RespondAsync("You don't have permission to use this command. (Command usable by Admins, and users with Manage Messages Permission. IE. Moderator Role)", ephemeral: true);
                    }
                }



                else if (slashCommand.Data.Name == "coinflip")
                {
                    Random random = new Random();
                    bool isHeads = random.Next(2) == 0;
                    string result = isHeads ? "Heads" : "Tails";
                    var embed = new EmbedBuilder
                    {
                        Title = "🪙  Coin Flip Result  🪙",
                        Description = $"The coin landed on: **{result}**",
                        Color = Color.DarkRed,
                    };
                    await slashCommand.RespondAsync(embed: embed.Build());

                }
                else
                {
                    Console.WriteLine("Unexpected error in Command Handling.");
                }

            }
        }



        //new method
        private async Task RegisterSlashCommands()
        {

            var commands = new List<SlashCommandBuilder>
    {

              new SlashCommandBuilder()
            .WithName("givexp")
            .WithDescription("Give XP to a user.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to give XP to.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("amount")
                .WithDescription("The amount of XP to give.")
                .WithType(ApplicationCommandOptionType.Integer)
                .WithRequired(true)),
                 new SlashCommandBuilder()
            .WithName("giveallxp")
            .WithDescription("Give XP to all users.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("amount")
                .WithDescription("The amount of XP to give.")
                .WithType(ApplicationCommandOptionType.Integer)
                .WithRequired(true)),

                  new SlashCommandBuilder()
            .WithName("verify")
            .WithDescription("Manually verify a user, and give them the server access AutoRole.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to manually verify.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true)),

               new SlashCommandBuilder()
            .WithName("removexp")
            .WithDescription("Remove XP from a user.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to remove XP from.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("amount")
                .WithDescription("The amount of XP to remove.")
                .WithType(ApplicationCommandOptionType.Integer)
                .WithRequired(true)),

               new SlashCommandBuilder()
            .WithName("coinflip")
            .WithDescription("Flip a coin, Heads or Tails."),

                new SlashCommandBuilder()
            .WithName("mute")
            .WithDescription("Mute a user for a specified duration.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to mute")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("duration")
                .WithDescription("The duration in minutes")
                .WithType(ApplicationCommandOptionType.Integer)
                .WithRequired(true)),

                 new SlashCommandBuilder()
            .WithName("unmute")
            .WithDescription("Unmute a user.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to mute")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true)),


            new SlashCommandBuilder()
            .WithName("roll")
            .WithDescription("Roll the dice"),

         new SlashCommandBuilder()
            .WithName("googleit")
            .WithDescription("Send a Google link for users that won't Google it.")
            .AddOption(new SlashCommandOptionBuilder()
            .WithName("message")
            .WithDescription("The search you'd like to share to the user.")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(true)),


         new SlashCommandBuilder()
            .WithName("say")
            .WithDescription("Make the bot say something.")
            .AddOption(new SlashCommandOptionBuilder()
            .WithName("message")
            .WithDescription("The message you'd like the bot to say.")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(true)),

           new SlashCommandBuilder()
            .WithName("yt")
            .WithDescription("Post a YouTube link")
            .AddOption(new SlashCommandOptionBuilder()
            .WithName("query")
            .WithDescription("Search for a video, or song.")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(true)),

           new SlashCommandBuilder()
        .WithName("live")
        .WithDescription("Alert when a user is live on Twitch")
        .AddOption(new SlashCommandOptionBuilder()
            .WithName("twitch-name")
            .WithDescription("Your Twitch Username")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(true))
        .AddOption(new SlashCommandOptionBuilder()
            .WithName("game-name")
            .WithDescription("The game being played")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(true)),

         new SlashCommandBuilder()
    .WithName("purge")
    .WithDescription("Delete a specified number of messages from the channel (with optional filtering).")
    .AddOption(new SlashCommandOptionBuilder()
        .WithName("messages")
        .WithDescription("The number of messages to delete.")
        .WithType(ApplicationCommandOptionType.Integer)
        .WithRequired(true))
    .AddOption(new SlashCommandOptionBuilder()
        .WithName("user")
        .WithDescription("Delete messages from a specific user.")
        .WithType(ApplicationCommandOptionType.User)
        .WithRequired(false))
    .AddOption(new SlashCommandOptionBuilder()
        .WithName("keyword")
        .WithDescription("Delete messages containing a specific keyword/phrase.")
        .WithType(ApplicationCommandOptionType.String)
        .WithRequired(false))
    .AddOption(new SlashCommandOptionBuilder()
        .WithName("bots-only")
        .WithDescription("Delete messages from bots only.")
        .WithType(ApplicationCommandOptionType.Boolean)
        .WithRequired(false)),

            new SlashCommandBuilder()
            .WithName("pm")
            .WithDescription("Send a PM to a user.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to PM.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
                .AddOption(new SlashCommandOptionBuilder()
                .WithName("message")
                .WithDescription("The message to send.")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(true)),

            new SlashCommandBuilder()
            .WithName("8ball")
            .WithDescription("Ask the magic 8 ball a question.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("question")
                .WithDescription("The question you want to ask")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(true)),

        new SlashCommandBuilder()
            .WithName("help")
            .WithDescription("Display information about VoidBot, and its features."),

        new SlashCommandBuilder()
            .WithName("level")
            .WithDescription("Check and display your Server Level"),

        new SlashCommandBuilder()
            .WithName("leaderboard")
            .WithDescription("Check and display the Server Leaderboard"),

        new SlashCommandBuilder()
        .WithName("rank")
        .WithDescription("Display user rank information.")
        .AddOption(new SlashCommandOptionBuilder()
            .WithName("user")
            .WithDescription("The user to display rank information for.")
            .WithType(ApplicationCommandOptionType.User)
            .WithRequired(true)),

            new SlashCommandBuilder()
            .WithName("kick")
            .WithDescription("Kick a user from the server.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to kick.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true)),

        new SlashCommandBuilder()
            .WithName("softban")
            .WithDescription("Softban a user from the server.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to softban.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("reason")
                .WithDescription("The reason for the softban.")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(false)),

        new SlashCommandBuilder()
            .WithName("ban")
            .WithDescription("Ban a user from the server.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to ban.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("reason")
                .WithDescription("The reason for the ban.")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(false)),

            new SlashCommandBuilder()
            .WithName("setup")
            .WithDescription("Start the setup wizard for VoidBot.(Admin/Owners Only)"),

         new SlashCommandBuilder()
            .WithName("warn")
            .WithDescription("Issue a warning to a user.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("The user to warn.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("reason")
                .WithDescription("The reason for the warning.")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(true)),

         new SlashCommandBuilder()
            .WithName("warninfo")
            .WithDescription("View recent warning reason for specified user.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("Check most recent warning for specified user.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true)),

         new SlashCommandBuilder()
            .WithName("warnclear")
            .WithDescription("Clear a specified users warnings.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("user")
                .WithDescription("Clear a specified users warnings.")
                .WithType(ApplicationCommandOptionType.User)
                .WithRequired(true))
         .AddOption(new SlashCommandOptionBuilder()
                .WithName("reason")
                .WithDescription("Select Warning ID to Remove.")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(true)),

        new SlashCommandBuilder()
    .WithName("duel")
    .WithDescription("Challenge a user to a duel.")
    .AddOption(new SlashCommandOptionBuilder()
        .WithName("user")
        .WithDescription("The user to duel.")
        .WithType(ApplicationCommandOptionType.User)
        .WithRequired(true)),

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
                    WelcomeChannelId = 0,
                    RulesChannelId = 0,
                    WarnPingChannelId = 0,
                    AutoRoleId = 0,
                    StreamerRole = 0,
                    InviteURL = "",
                    Prefix = "",
                    XPCooldown = 60,
                    XPAmount = 10,
                    XPSystemEnabled = true,
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
                .Set(s => s.WelcomeChannelId, serverSettings.WelcomeChannelId)
                .Set(s => s.RulesChannelId, serverSettings.RulesChannelId)
                .Set(s => s.WarnPingChannelId, serverSettings.WarnPingChannelId)
                .Set(s => s.AutoRoleId, serverSettings.AutoRoleId)
                .Set(s => s.StreamerRole, serverSettings.StreamerRole)
                .Set(s => s.InviteURL, serverSettings.InviteURL)
                .Set(s => s.Prefix, serverSettings.Prefix)
                .Set(s => s.XPSystemEnabled, serverSettings.XPSystemEnabled)
                .Set(s => s.XPCooldown, serverSettings.XPCooldown)
                .Set(s => s.XPAmount, serverSettings.XPAmount);

            var options = new UpdateOptions { IsUpsert = true };

            await _serverSettingsCollection.UpdateOneAsync(filter, update, options);
        }

        private async Task UserJoined(SocketGuildUser user)
        {
            var serverId = user.Guild.Id;

            var serverSettings = await GetServerSettings(serverId);

            if (serverSettings == null)
            {
                Console.WriteLine("Server settings not found.");
                return;
            }

            var channels = user.Guild.Channels;

            // Find the channels using IDs from MongoDB settings
            var rulesChannel = channels.OfType<ITextChannel>().FirstOrDefault(c => c.Id == serverSettings.RulesChannelId);
            var VerifyWelcomeChannel = channels.OfType<ITextChannel>().FirstOrDefault(c => c.Id == serverSettings.WelcomeChannelId);

            if (rulesChannel == null || VerifyWelcomeChannel == null)
            {
                Console.WriteLine("Rules or Welcome channel not found.");
                return;
            }

            string[] welcomeMessages = new string[]
            {
        $"HEYO! Welcome to the server {user.Mention}! Be sure to read the Rules in the {rulesChannel.Mention}!",
        $"Greetings {user.Mention}! Enjoy your stay and don't forget to check out the Rules in {rulesChannel.Mention}.",
        $"Welcome to the server {user.Mention}! Enjoy your stay and don't forget to check out the Rules in {rulesChannel.Mention}.",
        $"Welcome, {user.Mention}! We're thrilled to have you in the server! Check out the Rules in {rulesChannel.Mention}.",
        $"Hey there, {user.Mention}! Feel free to explore and have fun! Don't forget to familiarize yourself with the Rules in {rulesChannel.Mention}.",
        $"Greetings, {user.Mention}! Your presence makes our server even better! Take a moment to review the Rules in {rulesChannel.Mention}.",
        $"Hello, {user.Mention}! Get ready for an awesome experience! Don't skip the Rules in {rulesChannel.Mention}."
            };

            int randomIndex = new Random().Next(0, welcomeMessages.Length);
            string selectedWelcomeMessage = welcomeMessages[randomIndex];

            var welcomeEmbed = new EmbedBuilder
            {
                Title = $"👋 Welcome, {user.GlobalName}!",
                Color = Color.DarkRed,
                Description = selectedWelcomeMessage,
                Footer = new EmbedFooterBuilder
                {
                    Text = "\u200B\n┤|VoidBot Discord Bot|├\nhttps://voidbot.lol/",
                    IconUrl = "https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png"
                },
                Timestamp = DateTime.UtcNow,
                ThumbnailUrl = "https://raw.githubusercontent.com/V0idpool/VoidBot-Discord-Bot-GUI/main/Img/mat.png"
            };

            var verifyButton = new ButtonBuilder
            {
                Label = "✅ Verify",
                CustomId = $"initiate_verify_button_{user.Id}",
                Style = ButtonStyle.Success
            };

            var supportButton = new ButtonBuilder
            {
                Label = "❓ Support",
                CustomId = $"support_button_{user.Id}",
                Style = ButtonStyle.Primary
            };

            var component = new ComponentBuilder()
                .WithButton(verifyButton)
                .WithButton(supportButton)
                .Build();

            var welcomeMessage = await VerifyWelcomeChannel.SendMessageAsync(embed: welcomeEmbed.Build(), components: component);
            var pingMessage = await VerifyWelcomeChannel.SendMessageAsync($"{user.Mention}, Please verify your account using the button above.");

            var userContext = new UserContext
            {
                Id = ObjectId.GenerateNewId(),
                ServerId = serverId,
                UserId = user.Id,
                WelcomeMessageId = welcomeMessage.Id,
                PingMessageId = pingMessage.Id
            };

            // Update the user context in MongoDB
            await UserContextStore.AddOrUpdateAsync(userContext);
            // Log user information
            LogUserInformation(user);
        }
        // Log the available user information
        private void LogUserInformation(SocketGuildUser user)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine($"User Joined: {user.Username}#{user.Discriminator}");
            Console.WriteLine($"User ID: {user.Id}");
            Console.WriteLine($"Join Time: {DateTime.UtcNow}");
            Console.WriteLine($"Guild: {user.Guild.Name}");
            Console.WriteLine($"Account Creation Date: {user.CreatedAt}");
            Console.WriteLine($"Joined Server: {user.JoinedAt?.ToString() ?? "Unknown"}");
            Console.WriteLine("==================================================");
        }

        [BsonIgnoreExtraElements]
        public class ServerSettings
        {
            [BsonId]
            public ObjectId Id { get; set; }
            public ulong ServerId { get; set; }
            public ulong WelcomeChannelId { get; set; }
            public string WelcomeMessage { get; set; }
            public ulong RulesChannelId { get; set; }
            public ulong WarnPingChannelId { get; set; }
            public ulong AutoRoleId { get; set; }
            public ulong StreamerRole { get; set; }
            public string InviteURL { get; set; }
            public string Prefix { get; set; }
            public int XPCooldown { get; set; }
            public int XPAmount { get; set; }
            public bool XPSystemEnabled { get; set; }
        }

        public class UserContext
        {
            [BsonId]
            public ObjectId Id { get; set; }
            public ulong ServerId { get; set; }
            public ulong UserId { get; set; }
            public bool HasVerified { get; set; }
            public ulong WelcomeMessageId { get; set; }
            public ulong PingMessageId { get; set; }
        }

        public static class UserContextStore
        {
            private static IMongoCollection<UserContext> _userContextCollection;

            public static void Initialize(IMongoDatabase database)
            {
                _userContextCollection = database.GetCollection<UserContext>("UserContexts");
            }

            // Save user context
            public static async Task AddOrUpdateAsync(UserContext context)
            {
                var filter = Builders<UserContext>.Filter.And(
                    Builders<UserContext>.Filter.Eq(u => u.ServerId, context.ServerId),
                    Builders<UserContext>.Filter.Eq(u => u.UserId, context.UserId)
                );

                await _userContextCollection.ReplaceOneAsync(
                    filter,
                    context,
                    new ReplaceOptions { IsUpsert = true }
                );
            }

            // Get user context
            public static async Task<UserContext> GetAsync(ulong serverId, ulong userId)
            {
                var filter = Builders<UserContext>.Filter.And(
                    Builders<UserContext>.Filter.Eq(u => u.ServerId, serverId),
                    Builders<UserContext>.Filter.Eq(u => u.UserId, userId)
                );

                return await _userContextCollection.Find(filter).FirstOrDefaultAsync();
            }

            // Delete user context (optional)
            public static async Task DeleteAsync(ulong serverId, ulong userId)
            {
                var filter = Builders<UserContext>.Filter.And(
                    Builders<UserContext>.Filter.Eq(u => u.ServerId, serverId),
                    Builders<UserContext>.Filter.Eq(u => u.UserId, userId)
                );

                await _userContextCollection.DeleteOneAsync(filter);
            }
        }

        private int RollDice()
        {
            return new Random().Next(1, 7);
        }

    }
}
