using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OblivionUtils.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace OblivionUtils
{
    public static class Program
    {
        public static DiscordSocketClient _client = null!;
        public static SocketGuild _guild = null!;
        private static readonly CommandService _commands;

        private static readonly string _botToken;
        public static readonly string OAuthURL;
        public static readonly string updateKey;

        public static List<LinkedAccount> linkedAccounts = new();
        public static Dictionary<string, LinkedAccount> linkedAccountLookupMc = new();
        public static Dictionary<ulong, LinkedAccount> linkedAccountLookupDiscord = new();

        public static Dictionary<LinkedAccount, Rank> accountRanks = new();

        private static Dictionary<ulong, Rank> donorRoles = new();
        public static Dictionary<string, Rank> donorRolesMCLookup = new();

        public static readonly DatabaseManager accountDatabase;
        public static readonly DatabaseManager luckpermsDatabase;

        private static readonly ulong staffRoleID;
        public static SocketRole staffRole { private set; get; }

        static Program()
        {
            _commands = new CommandService(new CommandServiceConfig
            {
                LogLevel = LogSeverity.Info,
                CaseSensitiveCommands = false,
            });
            using (StreamReader r = new StreamReader("config.json"))
            {
                string jsonData = r.ReadToEnd();
                dynamic json = JsonConvert.DeserializeObject(jsonData)!;
                try
                {
                    _botToken = json.botToken;
                    OAuthURL = json.oauthURL;
                    staffRoleID = json.staff_role_id;
                    updateKey = json.accountLinking.updateKey;

                    foreach (dynamic rankInfo in json.accountLinking.donatorRoles)
                    {
                        var rank = new Rank()
                        {
                            minecraftRank = rankInfo.mcRank,
                            discordRoleID = rankInfo.roleID,
                            discordRoleName = rankInfo.roleName,
                            weight = rankInfo.weight
                        };
                        donorRoles.Add((ulong)rankInfo.roleID, rank);
                        donorRolesMCLookup.Add((string)rankInfo.mcRank, rank);
                    }

                    foreach (Rank rank in donorRoles.Values)
                    {
                        Logger.Log(LogLevel.Debug, $"Loaded Role \"{rank.discordRoleName}\" ID: {rank.discordRoleID} mcRank: {rank.minecraftRank} Weight: {rank.weight} ");
                    }

                    if (json.accountLinking.linkingDatabase.port < 65535 && json.accountLinking.luckPermsDatabase.port < 65535)
                    {
                        accountDatabase = new DatabaseManager(new DatabaseInformation()
                        {
                            User = json.accountLinking.linkingDatabase.username,
                            Password = json.accountLinking.linkingDatabase.password,
                            Host = json.accountLinking.linkingDatabase.host,
                            Port = json.accountLinking.linkingDatabase.port,
                            Database = json.accountLinking.linkingDatabase.database
                        });
                        luckpermsDatabase = new DatabaseManager(new DatabaseInformation()
                        {
                            User = json.accountLinking.luckPermsDatabase.username,
                            Password = json.accountLinking.luckPermsDatabase.password,
                            Host = json.accountLinking.luckPermsDatabase.host,
                            Port = json.accountLinking.luckPermsDatabase.port,
                            Database = json.accountLinking.luckPermsDatabase.database
                        });
                    }
                    else
                    {
                        throw new InvalidDataException("Port is not a number between 1-65535, exiting");
                    }
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException e)
                {
                    Logger.Log(LogLevel.Critical, "Incorrect Configuration File");
                    Logger.Log(LogLevel.Error, e.Message);
                    System.Environment.Exit(-1);
                }
            }
        }

        public static void Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(builder =>
                    builder.ClearProviders())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel();
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseUrls("http://0.0.0.0:9999/");
                })
                .Build();

            _client = new DiscordSocketClient(new DiscordSocketConfig()
            {
                LogLevel = LogSeverity.Info,
                AlwaysDownloadUsers = true,
            });

            _client.Log += Logger.DiscordLogger;
            _client.GuildMembersDownloaded += InitializeDatabase;
            _client.LoggedIn += InitCommands;
            _client.GuildMemberUpdated += HandleUserUpdate;

            _client.LoginAsync(TokenType.Bot, _botToken).GetAwaiter().GetResult();
            _client.StartAsync().GetAwaiter().GetResult();

            host.Run();
        }

        private static async Task InitCommands()
        {
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), null);
            _client.MessageReceived += HandleCommandAsync;
        }

        private static async Task InitializeDatabase(SocketGuild guild)
        {
            _guild = guild;
            staffRole = guild.GetRole(staffRoleID);
            luckpermsDatabase.OnDatabaseInitialized += OnLuckPermsDatabaseInitialize;
            accountDatabase.OnDatabaseInitialized += OnAccountLinkDatabaseInitialize;
            await luckpermsDatabase.initialize();
            await accountDatabase.initialize();
        }

        private static Task OnLuckPermsDatabaseInitialize(MySqlConnector.MySqlConnection connection)
        {
            Logger.Log(LogLevel.Information, $"Successfully Connected to LuckPerms Database");
            return Task.CompletedTask;
        }

        private static async Task OnAccountLinkDatabaseInitialize(MySqlConnector.MySqlConnection connection)
        {
            Logger.Log(LogLevel.Information, $"Successfully Connected to Account Linking Database");
            var queryResult = await accountDatabase.ExecuteRawQueryStatement<LinkedAccount>("SELECT * FROM `linked_accounts`;");
            if (queryResult.Count > 0)
            {
                linkedAccounts = queryResult;
                var client = new HttpClient();
                var guild = _client.Guilds.First();
                foreach (LinkedAccount linkedAccount in linkedAccounts)
                {
                    var mcName = GetMCName(await (await client.GetAsync($"https://api.mojang.com/user/profile/{Convert.ToHexString(linkedAccount.minecraft_uuid)}")).Content.ReadAsByteArrayAsync());
                    var user = guild.GetUser(linkedAccount.discord_id);

                    linkedAccountLookupMc[Convert.ToHexString(linkedAccount.minecraft_uuid)] = linkedAccount;
                    linkedAccountLookupDiscord[linkedAccount.discord_id] = linkedAccount;

                    Rank? highestRankDiscord = null;
                    Rank? highestRankMinecraft = null;

                    foreach (SocketRole role in user.Roles)
                        if (donorRoles.TryGetValue(role.Id, out Rank rank))
                            if (highestRankDiscord is null || highestRankDiscord.weight < rank.weight)
                                highestRankDiscord = rank;

                    var luckpermsQueryResult = await luckpermsDatabase.ExecuteParameterizedQueryStatement<LuckPermsUserPermission>("SELECT `uuid`, `permission` FROM `luckperms_user_permissions` WHERE `uuid` = @u AND `value` = 1 AND `server` = 'global';", new() { { "u", Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower() } })!;

                    foreach (LuckPermsUserPermission group in luckpermsQueryResult)
                        if (donorRolesMCLookup.TryGetValue(group.permission.Substring(6), out Rank rank))
                            if (highestRankMinecraft is null || highestRankMinecraft.weight < rank.weight)
                                highestRankMinecraft = rank;

                    if (highestRankDiscord != highestRankMinecraft && highestRankDiscord != null && highestRankMinecraft != null)
                    {
                        if (highestRankDiscord.weight > highestRankMinecraft.weight)
                        {
                            await luckpermsDatabase.ExecuteParameterizedUpdateStatement("DELETE FROM `luckperms_user_permissions` WHERE `uuid` = @u AND `permission` = @p", new() { { "u", Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower() }, {"p", $"group.{highestRankMinecraft.minecraftRank}"} } ); //Remove old rank
                            await luckpermsDatabase.ExecuteParameterizedUpdateStatement("INSERT INTO `luckperms_user_permissions` (`uuid`, `permission`, `value`, `server`, `world`, `expiry`, `contexts`) VALUES (@u, @p, 1, 'global', 'global', 0, '{}')", new() { { "u", Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower() }, { "p", $"group.{highestRankDiscord.minecraftRank}" } }); //Remove old rank
                            accountRanks[linkedAccount] = highestRankDiscord;
                            Logger.Log(LogLevel.Debug, $"Minecraft: {mcName}:{Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower()} Discord: {user.Username}:{linkedAccount.discord_id} Rank: {highestRankDiscord.discordRoleName}");
                        }
                        else
                        {
                            await user.RemoveRoleAsync(highestRankDiscord.discordRoleID);
                            await user.AddRoleAsync(highestRankMinecraft.discordRoleID);
                            accountRanks[linkedAccount] = highestRankMinecraft;
                            Logger.Log(LogLevel.Debug, $"Minecraft: {mcName}:{Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower()} Discord: {user.Username}:{linkedAccount.discord_id} Rank: {highestRankMinecraft.discordRoleName}");
                        }
                    }
                    else if (highestRankMinecraft != null)
                    {
                        accountRanks[linkedAccount] = highestRankMinecraft;
                        await user.AddRoleAsync(highestRankMinecraft.discordRoleID);
                        Logger.Log(LogLevel.Debug, $"Minecraft: {mcName}:{Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower()} Discord: {user.Username}:{linkedAccount.discord_id} Rank: {highestRankMinecraft.discordRoleName}");
                    }
                    else if (highestRankDiscord != null)
                    {
                        accountRanks[linkedAccount] = highestRankDiscord;
                        await luckpermsDatabase.ExecuteParameterizedUpdateStatement("INSERT INTO `luckperms_user_permissions` (`uuid`, `permission`, `value`, `server`, `world`, `expiry`, `contexts`) VALUES (@u, @p, 1, 'global', 'global', 0, '{}')", new() { { "u", Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower() }, { "p", $"group.{highestRankDiscord.minecraftRank}" } }); //Remove old rank
                        Logger.Log(LogLevel.Debug, $"Minecraft: {mcName}:{Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower()} Discord: {user.Username}:{linkedAccount.discord_id} Rank: {highestRankDiscord.discordRoleName}");
                    }
                    else
                        Logger.Log(LogLevel.Debug, $"Minecraft: {mcName}:{Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower()} Discord: {user.Username}:{linkedAccount.discord_id}");
                }
            }
        }

        private static async Task HandleCommandAsync(SocketMessage arg)
        {
            if (arg is SocketUserMessage)
            {
                SocketUserMessage msg = (arg as SocketUserMessage)!;

                if (msg == null) return;

                if (msg.Author.IsBot) return;

                int pos = 0;
                if (msg.HasCharPrefix('!', ref pos))
                {
                    var context = new SocketCommandContext(_client, msg);

                    var result = await _commands.ExecuteAsync(context, pos, null);

                    if (!result.IsSuccess && result.Error != CommandError.UnknownCommand)
                        await arg.Channel.SendMessageAsync(result.ErrorReason);
                }
            }
        }

        private static async Task HandleUserUpdate(SocketGuildUser before, SocketGuildUser after)
        {
        }

        private static Dictionary<String, T> Dyn2Dict<T>(dynamic dynObj)
        {
            var dictionary = new Dictionary<string, T>();
            foreach (PropertyDescriptor propertyDescriptor in TypeDescriptor.GetProperties(dynObj))
            {
                T obj = propertyDescriptor.GetValue(dynObj);
                dictionary.Add(propertyDescriptor.Name, obj);
            }
            return dictionary;
        }

        private static String GetMCName(Byte[] Json)
        {
            Utf8JsonReader Reader = new Utf8JsonReader(Json);

            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            return Reader.GetString()!;
        }
    }

    public class LinkedAccount
    {
        public ulong discord_id;
        public Byte[] minecraft_uuid;
    }

    public class LuckPermsUserPermission
    {
        public string uuid;
        public string permission;
    }

    public class Rank
    {
        public string minecraftRank;
        public ulong discordRoleID;
        public string discordRoleName;
        public Int16 weight;
    }
}