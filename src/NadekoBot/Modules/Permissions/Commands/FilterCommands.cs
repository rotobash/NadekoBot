using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Attributes;
using NadekoBot.Extensions;
using NadekoBot.Services;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Permissions
{
    public partial class Permissions
    {
        [Group]
        public class FilterCommands : ModuleBase
        {
            public static ConcurrentHashSet<ulong> InviteFilteringChannels { get; }
            public static ConcurrentHashSet<ulong> InviteFilteringServers { get; }

            //serverid, filteredwords
            private static ConcurrentDictionary<ulong, ConcurrentHashSet<Regex>> ServerFilteredWords { get; }

            public static ConcurrentHashSet<ulong> WordFilteringChannels { get; }
            public static ConcurrentHashSet<ulong> WordFilteringServers { get; }

            public static ConcurrentHashSet<Regex> FilteredWordsForChannel(ulong channelId, ulong guildId)
            {
                ConcurrentHashSet<Regex> words = new ConcurrentHashSet<Regex>();
                if(WordFilteringChannels.Contains(channelId))
                    ServerFilteredWords.TryGetValue(guildId, out words);
                return words;
            }

            public static ConcurrentHashSet<Regex> FilteredWordsForServer(ulong guildId)
            {
                var words = new ConcurrentHashSet<Regex>();
                if(WordFilteringServers.Contains(guildId))
                    ServerFilteredWords.TryGetValue(guildId, out words);
                return words;
            }

            static FilterCommands()
            {
                var guildConfigs = NadekoBot.AllGuildConfigs;

                InviteFilteringServers = new ConcurrentHashSet<ulong>(guildConfigs.Where(gc => gc.FilterInvites).Select(gc => gc.GuildId));
                InviteFilteringChannels = new ConcurrentHashSet<ulong>(guildConfigs.SelectMany(gc => gc.FilterInvitesChannelIds.Select(fci => fci.ChannelId)));

                var dict = guildConfigs.ToDictionary(gc => gc.GuildId, gc => new ConcurrentHashSet<Regex>(gc.FilteredWords.Select(fw => new Regex(fw.Word, RegexOptions.Compiled & RegexOptions.IgnoreCase))));

                ServerFilteredWords = new ConcurrentDictionary<ulong, ConcurrentHashSet<Regex>>(dict);

                var serverFiltering = guildConfigs.Where(gc => gc.FilterWords);
                WordFilteringServers = new ConcurrentHashSet<ulong>(serverFiltering.Select(gc => gc.GuildId));

                WordFilteringChannels = new ConcurrentHashSet<ulong>(guildConfigs.SelectMany(gc => gc.FilterWordsChannelIds.Select(fwci => fwci.ChannelId)));

            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task SrvrFilterInv()
            {
                var channel = (ITextChannel)Context.Channel;

                bool enabled;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id, set => set);
                    enabled = config.FilterInvites = !config.FilterInvites;
                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                if (enabled)
                {
                    InviteFilteringServers.Add(channel.Guild.Id);
                    await channel.SendConfirmAsync("Invite filtering enabled on this server.").ConfigureAwait(false);
                }
                else
                {
                    InviteFilteringServers.TryRemove(channel.Guild.Id);
                    await channel.SendConfirmAsync("Invite filtering disabled on this server.").ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task ChnlFilterInv()
            {
                var channel = (ITextChannel)Context.Channel;

                int removed;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id, set => set.Include(gc => gc.FilterInvitesChannelIds));
                    removed = config.FilterInvitesChannelIds.RemoveWhere(fc => fc.ChannelId == channel.Id);
                    if (removed == 0)
                    {
                        config.FilterInvitesChannelIds.Add(new Services.Database.Models.FilterChannelId()
                        {
                            ChannelId = channel.Id
                        });
                    }
                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                if (removed == 0)
                {
                    InviteFilteringChannels.Add(channel.Id);
                    await channel.SendConfirmAsync("Invite filtering enabled on this channel.").ConfigureAwait(false);
                }
                else
                {
                    InviteFilteringChannels.TryRemove(channel.Id);
                    await channel.SendConfirmAsync("Invite filtering disabled on this channel.").ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task SrvrFilterWords()
            {
                var channel = (ITextChannel)Context.Channel;

                bool enabled;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id, set => set);
                    enabled = config.FilterWords = !config.FilterWords;
                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                if (enabled)
                {
                    WordFilteringServers.Add(channel.Guild.Id);
                    await channel.SendConfirmAsync("Word filtering enabled on this server.").ConfigureAwait(false);
                }
                else
                {
                    WordFilteringServers.TryRemove(channel.Guild.Id);
                    await channel.SendConfirmAsync("Word filtering disabled on this server.").ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task ChnlFilterWords()
            {
                var channel = (ITextChannel)Context.Channel;

                int removed;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id, set => set.Include(gc => gc.FilterWordsChannelIds));
                    removed = config.FilterWordsChannelIds.RemoveWhere(fc => fc.ChannelId == channel.Id);
                    if (removed == 0)
                    {
                        config.FilterWordsChannelIds.Add(new Services.Database.Models.FilterChannelId()
                        {
                            ChannelId = channel.Id
                        });
                    }
                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                if (removed == 0)
                {
                    WordFilteringChannels.Add(channel.Id);
                    await channel.SendConfirmAsync("Word filtering enabled on this channel.").ConfigureAwait(false);
                }
                else
                {
                    WordFilteringChannels.TryRemove(channel.Id);
                    await channel.SendConfirmAsync("Word filtering disabled on this channel.").ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task FilterWord([Remainder] string word)
            {
                var channel = (ITextChannel)Context.Channel;
                

                if (string.IsNullOrWhiteSpace(word))
                    return;

                int removed;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id, set => set.Include(gc => gc.FilteredWords));

                    removed = config.FilteredWords.RemoveWhere(fw => fw.Word == word);

                    if (removed == 0)
                        config.FilteredWords.Add(new Services.Database.Models.FilteredWord() { Word = word });

                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                var filteredWords = ServerFilteredWords.GetOrAdd(channel.Guild.Id, new ConcurrentHashSet<Regex>());

                if (removed == 0)
                {
                    filteredWords.Add(new Regex(word, RegexOptions.Compiled & RegexOptions.IgnoreCase));
                    await channel.SendConfirmAsync($"Word `{word}` successfully added to the list of filtered words.")
                            .ConfigureAwait(false);
                }
                else
                {
                    filteredWords.TryRemove(new Regex(word, RegexOptions.Compiled & RegexOptions.IgnoreCase));
                    await channel.SendConfirmAsync($"Word `{word}` removed from the list of filtered words.")
                            .ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task LstFilterWords()
            {
                var channel = (ITextChannel)Context.Channel;

                ConcurrentHashSet<Regex> filteredWords;
                ServerFilteredWords.TryGetValue(channel.Guild.Id, out filteredWords);

                await channel.SendConfirmAsync($"List of filtered words", string.Join("\n", filteredWords))
                        .ConfigureAwait(false);
            }
        }
    }
}
