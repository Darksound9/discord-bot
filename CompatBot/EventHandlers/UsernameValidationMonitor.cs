﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers
{
    public static class UsernameValidationMonitor
    {
        public static Task OnMemberUpdated(GuildMemberUpdateEventArgs args) => UpdateDisplayName(args.Guild, args.Member);
        public static Task OnMemberAdded(GuildMemberAddEventArgs args) => UpdateDisplayName(args.Guild, args.Member);

        private static async Task UpdateDisplayName(DiscordGuild guild, DiscordMember guildMember)
        {
            try
            {
                if (guildMember.IsWhitelisted())
                    return;

                if (!(guild.Permissions?.HasFlag(Permissions.ChangeNickname) ?? true))
                    return;

                using var db = new BotDb();
                var forcedNickname = await db.ForcedNicknames.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == guildMember.Id && x.GuildId == guildMember.Guild.Id).ConfigureAwait(false);
                if (forcedNickname is null)
                    return;
                    
                if (guildMember.DisplayName == forcedNickname.Nickname)
                    return;

                await guildMember.ModifyAsync(mem => mem.Nickname = forcedNickname.Nickname).ConfigureAwait(false);
                Config.Log.Info($"Enforced nickname {forcedNickname.Nickname} for user {guildMember.Id} ({guildMember.Username}#{guildMember.Discriminator})");
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        public static async Task MonitorAsync(DiscordClient client, bool once = false)
        {
            do
            {
                if (!once)
                    await Task.Delay(Config.ForcedNicknamesRecheckTime, Config.Cts.Token).ConfigureAwait(false);
                if (await Moderation.Audit.CheckLock.WaitAsync(0).ConfigureAwait(false))
                    try
                    {
                        foreach (var guild in client.Guilds.Values)
                        {
                            try
                            {
                                if (!(guild.Permissions?.HasFlag(Permissions.ChangeNickname) ?? true))
                                    continue;

                                using var db = new BotDb();
                                var forcedNicknames = await db.ForcedNicknames
                                    .Where(mem => mem.GuildId == guild.Id)
                                    .ToListAsync()
                                    .ConfigureAwait(false);
                                if (forcedNicknames.Count == 0)
                                    continue;

                                foreach (var forced in forcedNicknames)
                                {
                                    var member = client.GetMember(guild, forced.UserId);
                                    if (member.DisplayName != forced.Nickname)
                                        try
                                        {
                                            await member.ModifyAsync(mem => mem.Nickname = forced.Nickname).ConfigureAwait(false);
                                            Config.Log.Info($"Enforced nickname {forced.Nickname} for user {member.Id} ({member.Username}#{member.Discriminator})");
                                        }
                                        catch {}
                                }
                            }
                            catch (Exception e)
                            {
                                Config.Log.Error(e);
                            }
                        }
                    }
                    finally
                    {
                        Moderation.Audit.CheckLock.Release();
                    }
            } while (!Config.Cts.IsCancellationRequested && !once);
        }
    }
}