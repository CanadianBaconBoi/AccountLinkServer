using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace OblivionUtils.Commands
{
    public class UnlinkCommand : ModuleBase<SocketCommandContext>
    {
        [Command("unlink")]
        [Summary("Unlinks your Discord account from your Minecraft account")]
        public async Task UnlinkAccountAsync()
        {
            var member = Context.Guild.GetUser(Context.User.Id);
            if (Program.linkedAccountLookupDiscord.ContainsKey(member.Id))
            {
                var linkedAccount = Program.linkedAccountLookupDiscord[member.Id];
                var uuid = Convert.ToHexString(linkedAccount.minecraft_uuid);

                await Program.accountDatabase.ExecuteParameterizedUpdateStatement("DELETE FROM `linked_accounts` WHERE `discord_id` = @d AND `minecraft_uuid` = x@u", new() { { "d", member.Id }, { "u", uuid } });

                await member.RemoveRoleAsync(Program.accountRanks[Program.linkedAccountLookupMc[uuid]].discordRoleID);

                Program.linkedAccounts.Remove(Program.linkedAccountLookupMc[uuid]);
                Program.accountRanks.Remove(Program.linkedAccountLookupMc[uuid]);
                Program.linkedAccountLookupMc.Remove(uuid);
                Program.linkedAccountLookupDiscord.Remove(member.Id);

                var message = await Context.Channel.SendMessageAsync($"Succesfully unlinked from Minecraft account.");
                //await Task.Delay(3000);
                //await message.DeleteAsync();
            }
            else
            {
                var message = await Context.Channel.SendMessageAsync($"You do not have a linked account.");
                //await Task.Delay(3000);
                //await message.DeleteAsync();
            }
        }

        [Command("unlink")]
        [Summary("Unlinks your Discord account from your Minecraft account")]
        public async Task UnlinkAccountAsync(SocketGuildUser member)
        {
            bool permitted = false;

            foreach (SocketRole role in Context.Guild.GetUser(Context.User.Id).Roles)
                permitted = role == Program.staffRole;

            if(permitted)
            {
                if (Program.linkedAccountLookupDiscord.ContainsKey(member.Id))
                {
                    var linkedAccount = Program.linkedAccountLookupDiscord[member.Id];
                    var uuid = Convert.ToHexString(linkedAccount.minecraft_uuid);

                    await Program.accountDatabase.ExecuteParameterizedUpdateStatement("DELETE FROM `linked_accounts` WHERE `discord_id` = @d AND `minecraft_uuid` = x@u", new() { { "d", member.Id }, { "u", uuid } });

                    await member.RemoveRoleAsync(Program.accountRanks[Program.linkedAccountLookupMc[uuid]].discordRoleID);

                    Program.linkedAccounts.Remove(Program.linkedAccountLookupMc[uuid]);
                    Program.accountRanks.Remove(Program.linkedAccountLookupMc[uuid]);
                    Program.linkedAccountLookupMc.Remove(uuid);
                    Program.linkedAccountLookupDiscord.Remove(member.Id);

                    var message = await Context.Channel.SendMessageAsync($"Succesfully unlinked user {member.Username}.");
                    //await Task.Delay(3000);
                    //await message.DeleteAsync();
                } else
                {
                    var message = await Context.Channel.SendMessageAsync($"User {member.Username} does not have a linked account.");
                    //await Task.Delay(3000);
                    //await message.DeleteAsync();
                }
            } else
            {
                var message = await Context.Channel.SendMessageAsync("You need permission to run this command on another user.");
                //await Task.Delay(3000);
                //await message.DeleteAsync();
            }
        }
    }
}