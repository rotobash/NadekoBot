﻿using Discord;
using Discord.Commands;
using NadekoBot.Attributes;
using NadekoBot.Extensions;
using NadekoBot.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NadekoBot.Modules.Gambling
{
    public partial class Gambling
    {
        [Group]
        public class Slots : NadekoSubmodule
        {
            private static int _totalBet;
            private static int _totalPaidOut;

            private const int _alphaCutOut = byte.MaxValue / 3;

            //here is a payout chart
            //https://lh6.googleusercontent.com/-i1hjAJy_kN4/UswKxmhrbPI/AAAAAAAAB1U/82wq_4ZZc-Y/DE6B0895-6FC1-48BE-AC4F-14D1B91AB75B.jpg
            //thanks to judge for helping me with this

            private readonly IImagesService _images;

            public Slots()
            {
                _images = NadekoBot.Images;
            }

            public class SlotMachine
            {
                public const int MaxValue = 5;

                static readonly List<Func<int[], int>> _winningCombos = new List<Func<int[], int>>()
                {
                    //three flowers
                    (arr) => arr.All(a=>a==MaxValue) ? 30 : 0,
                    //three of the same
                    (arr) => !arr.Any(a => a != arr[0]) ? 10 : 0,
                    //two flowers
                    (arr) => arr.Count(a => a == MaxValue) == 2 ? 4 : 0,
                    //one flower
                    (arr) => arr.Any(a => a == MaxValue) ? 1 : 0,
                };

                public static SlotResult Pull()
                {
                    var numbers = new int[3];
                    for (var i = 0; i < numbers.Length; i++)
                    {
                        numbers[i] = new NadekoRandom().Next(0, MaxValue + 1);
                    }
                    var multi = 0;
                    foreach (var t in _winningCombos)
                    {
                        multi = t(numbers);
                        if (multi != 0)
                            break;
                    }

                    return new SlotResult(numbers, multi);
                }

                public struct SlotResult
                {
                    public int[] Numbers { get; }
                    public int Multiplier { get; }
                    public SlotResult(int[] nums, int multi)
                    {
                        Numbers = nums;
                        Multiplier = multi;
                    }
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [OwnerOnly]
            public async Task SlotStats()
            {
                //i remembered to not be a moron
                var paid = _totalPaidOut;
                var bet = _totalBet;

                if (bet <= 0)
                    bet = 1;

                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle("Slot Stats")
                    .AddField(efb => efb.WithName("Total Bet").WithValue(bet.ToString()).WithIsInline(true))
                    .AddField(efb => efb.WithName("Paid Out").WithValue(paid.ToString()).WithIsInline(true))
                    .WithFooter(efb => efb.WithText($"Payout Rate: {paid * 1.0 / bet * 100:f4}%"));

                await Context.Channel.EmbedAsync(embed).ConfigureAwait(false);
            }

            [NadekoCommand, Usage, Description, Aliases]
            [OwnerOnly]
            public async Task SlotTest(int tests = 1000)
            {
                if (tests <= 0)
                    return;
                //multi vs how many times it occured
                var dict = new Dictionary<int, int>();
                for (int i = 0; i < tests; i++)
                {
                    var res = SlotMachine.Pull();
                    if (dict.ContainsKey(res.Multiplier))
                        dict[res.Multiplier] += 1;
                    else
                        dict.Add(res.Multiplier, 1);
                }

                var sb = new StringBuilder();
                const int bet = 1;
                int payout = 0;
                foreach (var key in dict.Keys.OrderByDescending(x => x))
                {
                    sb.AppendLine($"x{key} occured {dict[key]} times. {dict[key] * 1.0f / tests * 100}%");
                    payout += key * dict[key];
                }
                await Context.Channel.SendConfirmAsync("Slot Test Results", sb.ToString(),
                    footer: $"Total Bet: {tests * bet} | Payout: {payout * bet} | {payout * 1.0f / tests * 100}%");
            }

            private static readonly HashSet<ulong> _runningUsers = new HashSet<ulong>();

            [NadekoCommand, Usage, Description, Aliases]
            public async Task Slot(int amount = 0)
            {
                if (!_runningUsers.Add(Context.User.Id))
                    return;
                try
                {
                    if (amount < 1)
                    {
                        await ReplyErrorLocalized("min_bet_limit", 1 + CurrencySign).ConfigureAwait(false);
                        return;
                    }

                    if (amount > 999)
                    {
                        GetText("slot_maxbet", 999 + CurrencySign);
                        await ReplyErrorLocalized("max_bet_limit", 999 + CurrencySign).ConfigureAwait(false);
                        return;
                    }

                    if (!await CurrencyHandler.RemoveCurrencyAsync(Context.User, "Slot Machine", amount, false))
                    {
                        await ReplyErrorLocalized("not_enough", CurrencySign).ConfigureAwait(false);
                        return;
                    }
                    Interlocked.Add(ref _totalBet, amount);
                    using (var bgFileStream = NadekoBot.Images.SlotBackground.ToStream())
                    {
                        var bgImage = new ImageSharp.Image(bgFileStream);

                        var result = SlotMachine.Pull();
                        int[] numbers = result.Numbers;
                        using (var bgPixels = bgImage.Lock())
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                using (var file = _images.SlotEmojis[numbers[i]].ToStream())
                                {
                                    var randomImage = new ImageSharp.Image(file);
                                    using (var toAdd = randomImage.Lock())
                                    {
                                        for (int j = 0; j < toAdd.Width; j++)
                                        {
                                            for (int k = 0; k < toAdd.Height; k++)
                                            {
                                                var x = 95 + 142 * i + j;
                                                int y = 330 + k;
                                                var toSet = toAdd[j, k];
                                                if (toSet.A < _alphaCutOut)
                                                    continue;
                                                bgPixels[x, y] = toAdd[j, k];
                                            }
                                        }
                                    }
                                }
                            }

                            var won = amount * result.Multiplier;
                            var printWon = won;
                            var n = 0;
                            do
                            {
                                var digit = printWon % 10;
                                using (var fs = NadekoBot.Images.SlotNumbers[digit].ToStream())
                                {
                                    var img = new ImageSharp.Image(fs);
                                    using (var pixels = img.Lock())
                                    {
                                        for (int i = 0; i < pixels.Width; i++)
                                        {
                                            for (int j = 0; j < pixels.Height; j++)
                                            {
                                                if (pixels[i, j].A < _alphaCutOut)
                                                    continue;
                                                var x = 230 - n * 16 + i;
                                                bgPixels[x, 462 + j] = pixels[i, j];
                                            }
                                        }
                                    }
                                }
                                n++;
                            } while ((printWon /= 10) != 0);

                            var printAmount = amount;
                            n = 0;
                            do
                            {
                                var digit = printAmount % 10;
                                using (var fs = _images.SlotNumbers[digit].ToStream())
                                {
                                    var img = new ImageSharp.Image(fs);
                                    using (var pixels = img.Lock())
                                    {
                                        for (int i = 0; i < pixels.Width; i++)
                                        {
                                            for (int j = 0; j < pixels.Height; j++)
                                            {
                                                if (pixels[i, j].A < _alphaCutOut)
                                                    continue;
                                                var x = 395 - n * 16 + i;
                                                bgPixels[x, 462 + j] = pixels[i, j];
                                            }
                                        }
                                    }
                                }
                                n++;
                            } while ((printAmount /= 10) != 0);
                        }

                        var msg = GetText("better_luck");
                        if (result.Multiplier != 0)
                        {
                            await CurrencyHandler.AddCurrencyAsync(Context.User, $"Slot Machine x{result.Multiplier}", amount * result.Multiplier, false);
                            Interlocked.Add(ref _totalPaidOut, amount * result.Multiplier);
                            if (result.Multiplier == 1)
                                msg = GetText("slot_single", CurrencySign, 1);
                            else if (result.Multiplier == 4)
                                msg = GetText("slot_two", CurrencySign, 4);
                            else if (result.Multiplier == 10)
                                msg = GetText("slot_three", 10);
                            else if (result.Multiplier == 30)
                                msg = GetText("slot_jackpot", 30);
                        }

                        await Context.Channel.SendFileAsync(bgImage.ToStream(), "result.png", Context.User.Mention + " " + msg + $"\n`{GetText("slot_bet")}:`{amount} `{GetText("slot_won")}:` {amount * result.Multiplier}{NadekoBot.BotConfig.CurrencySign}").ConfigureAwait(false);
                    }
                }
                finally
                {
                    var _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        _runningUsers.Remove(Context.User.Id);
                    });
                }
            }
        }
    }
}