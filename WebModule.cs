using Carter;
using Microsoft.AspNetCore.Http;
using OblivionUtils.Services;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OblivionUtils
{
    public class WebModule : CarterModule
    {
        private HttpClient client = new HttpClient();
        private string successPage;
        private string failPage;
        private string successUnlinkPage;
        private string failUnlinkPage;
        private string unauthorizedPage;
        private Byte[] logoImage;

        public WebModule()
        {
            using (Stream Resource = typeof(WebModule).Assembly.GetManifestResourceStream("OblivionUtils.assets.icon.png")!)
            {
                logoImage = new Byte[Resource.Length];
                Resource.Read(logoImage);
                Resource.Close();
            }
            successPage = File.ReadAllText("assets/success.html");
            failPage = File.ReadAllText("assets/fail.html");
            unauthorizedPage = File.ReadAllText("assets/401.html");

            successUnlinkPage = File.ReadAllText("assets/unlinksuccess.html");
            failUnlinkPage = File.ReadAllText("assets/unlinkfail.html");

            Get("/", async (req, res) =>
            {
                if (req.Query.ContainsKey("code"))
                {
                    var requestContent = new XFormDataBuilder()
                    {
                        {"client_id", 905422754117992480},
                        {"client_secret", "lmGzA_-UiqQanplbg0q5XpB200DQ2uGL"},
                        {"grant_type", "authorization_code"},
                        {"code", req.Query["code"][0]},
                        {"redirect_uri", @"http://accountlink.oblivionmc.us/"}
                    }.ToString();

                    var response = await client.PostAsync("https://discord.com/api/v8/oauth2/token", new StringContent(requestContent, Encoding.UTF8, "application/x-www-form-urlencoded"));
                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        Byte[] content = await response.Content.ReadAsByteArrayAsync();
                        Byte[] responseData;
                        using (var request =
                            new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v8/oauth2/@me"))
                        {
                            var token = GetBearerToken(content);
                            if (token == null)
                            {
                                await res.WriteAsync($"State: {req.Query["state"][0]}, Code: {req.Query["code"][0]}\nFailed to authenticate with Discord\nStatus Code: {response.StatusCode}\nRequest Data: {requestContent}\nResponse Data: {await response.Content.ReadAsStringAsync()}");
                                return;
                            }
                            request.Headers.Authorization =
                                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                            responseData = await (await client.SendAsync(request)).Content.ReadAsByteArrayAsync();
                            requestContent = new XFormDataBuilder()
                            {
                                {"client_id", 905422754117992480},
                                {"client_secret", "lmGzA_-UiqQanplbg0q5XpB200DQ2uGL"},
                                {"token", token}
                            }.ToString();
                            await client.PostAsync("https://discord.com/api/v8/oauth2/token/revoke", new StringContent(requestContent, Encoding.UTF8, "application/x-www-form-urlencoded"));
                        }
                        var queryResult = await Program.accountDatabase.ExecuteParameterizedScalarQueryStatement("SELECT `minecraft_uuid` FROM `tokens` WHERE `token_id`=x@t", new() { { "t", req.Query["state"][0].Replace("-", String.Empty) } });
                        if (queryResult is Byte[])
                        {
                            var uuid = Convert.ToHexString((queryResult as Byte[])!);
                            Logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"https://api.mojang.com/user/profile/{uuid}");

                            var mcUsername = GetMCName((await (await client.GetAsync($"https://api.mojang.com/user/profile/{uuid}")).Content.ReadAsByteArrayAsync()));
                            var userIDandAvatarID = GetUserIDAndAvatarID(responseData);
                            var user = Program._client.Guilds.First().GetUser(userIDandAvatarID.Item1);
                            await res.WriteAsync(successPage.Replace(@"{{AVATAR_URL}}", $"https://cdn.discordapp.com/avatars/{userIDandAvatarID.Item1}/{userIDandAvatarID.Item2}.png?size=512").Replace(@"{{DISCORD_NAME}}", $"{user.Username}#{user.Discriminator}").Replace(@"{{MINECRAFT_USERNAME}}", mcUsername));

                            await Program.accountDatabase.ExecuteParameterizedUpdateStatement("DELETE FROM `tokens` WHERE `token_id` = x@t", new() { { "t", req.Query["state"][0].Replace("-", String.Empty) } });
                            await Program.accountDatabase.ExecuteParameterizedUpdateStatement("INSERT INTO `linked_accounts` (`discord_id`, `minecraft_uuid`) VALUES (@d, x@u)", new() { { "d", userIDandAvatarID.Item1 }, { "u", uuid } });

                            var linkedAccount = new LinkedAccount()
                            {
                                discord_id = userIDandAvatarID.Item1,
                                minecraft_uuid = (queryResult as Byte[])!
                            };

                            var luckpermsQueryResult = await Program.luckpermsDatabase.ExecuteParameterizedQueryStatement<LuckPermsUserPermission>("SELECT `uuid`, `permission` FROM `luckperms_user_permissions` WHERE `uuid` = @u AND `value` = 1 AND `server` = 'global';", new() { { "u", Convert.ToHexString(linkedAccount.minecraft_uuid).Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-").ToLower() } })!;
                            Rank highestMinecraftRank = null;

                            foreach (LuckPermsUserPermission group in luckpermsQueryResult)
                                if (Program.donorRolesMCLookup.TryGetValue(group.permission.Substring(6), out Rank rank))
                                    if (highestMinecraftRank is null || highestMinecraftRank.weight < rank.weight)
                                        highestMinecraftRank = rank;

                            Program.linkedAccounts.Add(linkedAccount);
                            Program.linkedAccountLookupMc.Add(uuid, linkedAccount);
                            Program.linkedAccountLookupDiscord.Add(userIDandAvatarID.Item1, linkedAccount);
                            Program.accountRanks.Add(linkedAccount, highestMinecraftRank);

                            if (highestMinecraftRank != null)
                            {
                                await user.AddRoleAsync(highestMinecraftRank.discordRoleID);
                                Logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Linked user {user.Username} to {mcUsername} with rank {highestMinecraftRank.discordRoleName}");
                            }
                        }
                        else
                        {
                            Logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, $"Token {req.Query["state"][0]} does not exist.");
                            await res.WriteAsync(failPage);
                        }
                    }
                    else
                    {
                        await res.WriteAsync(failPage);
                    }
                }
                else if (req.Query.ContainsKey("error"))
                {
                    await res.WriteAsync(failPage);
                }
                else
                {
                    await res.WriteAsync(failPage);
                }
            });
            Get("/genoauth", async (req, res) =>
            {
                if (req.Query.ContainsKey("uuid"))
                {
                    if (System.Guid.TryParse(req.Query["uuid"][0], out System.Guid Token))
                    {
                        res.Redirect($"{Program.OAuthURL}&state={Token}");
                    }
                    else
                    {
                        await res.WriteAsync("Invalid UUID passed");
                    }
                }
            });

            Get("/unlink", async (req, res) =>
            {
                if (req.Query.ContainsKey("unlinkToken"))
                {
                    if (Guid.TryParse(req.Query["unlinkToken"][0], out Guid unlinkTokenID))
                    {
                        var queryResult = await Program.accountDatabase.ExecuteParameterizedQueryStatement<UnlinkToken>("SELECT `minecraft_uuid`, `discord_id` FROM `unlinktokens` WHERE `token_id`=x@t", new() { { "t", unlinkTokenID.ToString("N") } });
                        if (queryResult != null)
                        {
                            var unlinkToken = queryResult[0];
                            var uuid = Convert.ToHexString(unlinkToken.minecraft_uuid);
                            Logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"https://api.mojang.com/user/profile/{uuid}");

                            var mcUsername = GetMCName(await (await client.GetAsync($"https://api.mojang.com/user/profile/{uuid}")).Content.ReadAsByteArrayAsync());
                            var user = Program._client.Guilds.First().GetUser(unlinkToken.discord_id);
                            await res.WriteAsync(successUnlinkPage.Replace(@"{{AVATAR_URL}}", user.GetAvatarUrl(size: 512)).Replace(@"{{DISCORD_NAME}}", $"{user.Username}#{user.Discriminator}").Replace(@"{{MINECRAFT_USERNAME}}", mcUsername));

                            await Program.accountDatabase.ExecuteParameterizedUpdateStatement("DELETE FROM `unlinktokens` WHERE `token_id` = x@t", new() { { "t", unlinkTokenID.ToString("N") } });
                            await Program.accountDatabase.ExecuteParameterizedUpdateStatement("DELETE FROM `linked_accounts` WHERE `discord_id` = @d AND `minecraft_uuid` = x@u", new() { { "d", unlinkToken.discord_id }, { "u", uuid } });

                            await user.RemoveRoleAsync(Program.accountRanks[Program.linkedAccountLookupMc[uuid]].discordRoleID);

                            Program.linkedAccounts.Remove(Program.linkedAccountLookupMc[uuid]);
                            Program.accountRanks.Remove(Program.linkedAccountLookupMc[uuid]);
                            Program.linkedAccountLookupMc.Remove(uuid);
                            Program.linkedAccountLookupDiscord.Remove(user.Id);
                        }
                        else
                        {
                            Logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, $"Unlink token {unlinkTokenID.ToString("N")} does not exist.");
                            await res.WriteAsync(failUnlinkPage);
                        }
                    }
                    else
                    {
                        await res.WriteAsync(failUnlinkPage);
                    }
                }
                else
                {
                    await res.WriteAsync(failUnlinkPage);
                }
            });

            Get("/logo", async (req, res) =>
            {
                await res.BodyWriter.WriteAsync(logoImage);
            });

            Get("/update", async (req, res) =>
            {
                res.StatusCode = StatusCodes.Status401Unauthorized;
                res.WriteAsync(unauthorizedPage);
            });

            Post("/update", async (req, res) =>
            {
                if (req.Headers.ContainsKey("Authorization") && req.Headers.ContainsKey("User-Agent")
                    && req.Headers["Authorization"] == $"Bearer {Program.updateKey}" && req.Headers["User-Agent"] == "Oblivion AccountLink Plugin"
                    && req.HasFormContentType)
                {
                    var formData = await req.ReadFormAsync();
                    Logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Has Form Data: {formData.ContainsKey("target") && formData.ContainsKey("group") && formData.ContainsKey("action")} Target: {formData.ContainsKey("target")} Group: {formData.ContainsKey("group")} Action: {formData.ContainsKey("action")}");
                    if (formData.ContainsKey("target") && formData.ContainsKey("group") && formData.ContainsKey("action"))
                    {
                        if (Program.linkedAccountLookupMc.TryGetValue(formData["target"][0].Replace("-", String.Empty).ToUpper(), out LinkedAccount linkedAccount))
                        {
                            if (formData["action"][0] == "add")
                            {
                                var role = Program._guild.GetRole(Program.donorRolesMCLookup[formData["group"][0]].discordRoleID);
                                if (!Program._guild.GetUser(linkedAccount.discord_id).Roles.Contains(role))
                                {
                                    await Program._guild.GetUser(linkedAccount.discord_id).AddRoleAsync(Program.donorRolesMCLookup[formData["group"][0]].discordRoleID);
                                    res.StatusCode = StatusCodes.Status200OK;
                                    res.WriteAsync("OK, added role to user");
                                }
                                else
                                {
                                    res.StatusCode = StatusCodes.Status304NotModified;
                                }
                            }
                            else if (formData["action"][0] == "remove")
                            {
                                var role = Program._guild.GetRole(Program.donorRolesMCLookup[formData["group"][0]].discordRoleID);
                                if (Program._guild.GetUser(linkedAccount.discord_id).Roles.Contains(role))
                                {
                                    await Program._guild.GetUser(linkedAccount.discord_id).RemoveRoleAsync(Program.donorRolesMCLookup[formData["group"][0]].discordRoleID);
                                    res.StatusCode = StatusCodes.Status200OK;
                                    res.WriteAsync("OK, removed role to user");
                                }
                                else
                                {
                                    res.StatusCode = StatusCodes.Status304NotModified;
                                }
                            } else
                            {
                                res.StatusCode = StatusCodes.Status402PaymentRequired;
                                res.WriteAsync("<h1>400 Bad Request</h1>");
                            }
                        }
                    }
                    else
                    {
                        res.StatusCode = StatusCodes.Status402PaymentRequired;
                        await res.WriteAsync("<h1>400 Bad Request</h1>");
                    }
                }
                else
                {
                    res.StatusCode = StatusCodes.Status401Unauthorized;
                    await res.WriteAsync(unauthorizedPage);
                }
            });
        }

        public class UnlinkToken
        {
            public Byte[] minecraft_uuid;
            public ulong discord_id;
        }

        private static String GetBearerToken(Byte[] JsonData)
        {
            Utf8JsonReader Reader = new Utf8JsonReader(JsonData);

            Reader.Read();
            Reader.Read();
            Reader.Read();

            return Reader.GetString()!;
        }

        public String GetMCName(Byte[] Json)
        {
            Utf8JsonReader Reader = new Utf8JsonReader(Json);

            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            return Reader.GetString()!;
        }

        private static (UInt64, string) GetUserIDAndAvatarID(Byte[] JsonData)
        {
            Utf8JsonReader Reader = new Utf8JsonReader(JsonData);

            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Skip();
            Reader.Read();
            Reader.Skip();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            ulong a = UInt64.Parse(Reader.GetString()!); // UserID
            Reader.Read();
            Reader.Read();
            Reader.Read();
            Reader.Read();
            return (a, Reader.GetString()!); // Return UserID and AvatarID
        }
    }

    public class XFormDataBuilder : IEnumerable
    {
        private StringBuilder Output = new StringBuilder();

        public void Add(String Key, String Value)
        {
            if (Output.Length > 0)
                Output.Append('&');
            Output.Append(Key);
            Output.Append('=');
            Output.Append(Uri.EscapeDataString(Value));
        }

        public void Add<T>(String Key, T Value)
        {
            if (Output.Length > 0)
                Output.Append('&');
            Output.Append(Key);
            Output.Append('=');
            Output.Append(Uri.EscapeDataString(Value?.ToString() ?? String.Empty));
        }

        public override string ToString() => Output.ToString();

        public override bool Equals(object? obj)
        {
            return obj is null
                ? false
                : obj is XFormDataBuilder
                    ? Output.Equals((obj as XFormDataBuilder)!.Output)
                    : Output.Equals(obj);
        }

        public override int GetHashCode() => Output.GetHashCode();

        public IEnumerator GetEnumerator() => throw new NotSupportedException();
    }
}