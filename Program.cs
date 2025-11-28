using DatingBot;
using Telegram.Bot;
using Telegram.Bot.Extensions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

// replace YOUR_BOT_TOKEN below, or set your TOKEN in Project Properties > Debug > Launch profiles UI > Environment variables
var token = Environment.GetEnvironmentVariable("TOKEN") ?? "YOUR_BOT_TOKEN";

using var cts = new CancellationTokenSource();
var bot = new TelegramBotClient(token, cancellationToken: cts.Token);

var me = await bot.GetMe();

var dbPath = Path.Combine(AppContext.BaseDirectory, "datingbot.db");
var database = new Database(dbPath);
var handlers = new UpdateHandlers(bot, database);

await bot.DeleteWebhook();          // you may comment this line if you find it unnecessary
await bot.DropPendingUpdates();     // you may comment this line if you find it unnecessary

bot.OnError += (ex, source) => handlers.HandleErrorAsync(ex, source, cts.Token);
bot.OnMessage += handlers.HandleMessageAsync;
bot.OnUpdate += handlers.HandleUpdateAsync;


Console.WriteLine($"@{me.Username} is running... Press Escape to terminate");
while (Console.ReadKey(true).Key != ConsoleKey.Escape) ;
cts.Cancel(); // stop the bot


