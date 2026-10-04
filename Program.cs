using Discord;
using Discord.WebSocket;
using System.Text.Json;

class Program
{
    static DiscordSocketClient client = null!;

    static string discordServerId = "1311354688821792768";
    static string verifiedRoleId = "1311364927461527624";

    static async Task Main()
    {
        string? token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Bot token is missing.");
            return;
        }

        client = new DiscordSocketClient(new DiscordSocketConfig
        {
            UseInteractionSnowflakeDate = false
        });

        client.Log += Log;
        client.Ready += RegisterCommands;
        client.InteractionCreated += HandleInteraction;

        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();

        Console.WriteLine("Bot is running!");

        await Task.Delay(-1);
    }

    static async Task RegisterCommands()
    {
        ulong serverId = ulong.Parse(discordServerId);

        SocketGuild? guild = client.GetGuild(serverId);

        if (guild == null)
        {
            Console.WriteLine("Discord server not found.");
            return;
        }

        var command = new SlashCommandBuilder()
            .WithName("verify")
            .WithDescription("Verify a member's Mobile Legends account")
            .AddOption(
                new SlashCommandOptionBuilder()
                    .WithName("member")
                    .WithDescription("The member you want to verify")
                    .WithType(ApplicationCommandOptionType.User)
                    .WithRequired(true))
            .AddOption(
                new SlashCommandOptionBuilder()
                    .WithName("mlbb_id")
                    .WithDescription("The member's MLBB Player ID")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true))
            .AddOption(
                new SlashCommandOptionBuilder()
                    .WithName("server_id")
                    .WithDescription("The member's MLBB Server/Zone ID")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true));

        await guild.CreateApplicationCommandAsync(command.Build());

        Console.WriteLine("/verify command registered!");
    }

    static async Task HandleInteraction(SocketInteraction interaction)
    {
        if (interaction is SocketSlashCommand command)
        {
            if (command.Data.Name == "verify")
            {
                await VerifyMLBB(command);
            }
        }
    }

    static async Task VerifyMLBB(SocketSlashCommand command)
    {
        await command.DeferAsync();

        try
        {
            ulong serverId = ulong.Parse(discordServerId);

            SocketGuild? guild = client.GetGuild(serverId);

            if (guild == null)
            {
                await command.FollowupAsync(
                    "❌ Discord server could not be found."
                );

                return;
            }

            SocketGuildUser? targetMember = command.Data.Options
                .First(x => x.Name == "member")
                .Value as SocketGuildUser;

            if (targetMember == null)
            {
                await command.FollowupAsync(
                    "❌ The selected member could not be found."
                );

                return;
            }

            string mlbbId = command.Data.Options
                .First(x => x.Name == "mlbb_id")
                .Value.ToString()!;

            string serverZoneId = command.Data.Options
                .First(x => x.Name == "server_id")
                .Value.ToString()!;

            string apiUrl =
                $"https://api.isan.eu.org/nickname/ml?id={Uri.EscapeDataString(mlbbId)}&server={Uri.EscapeDataString(serverZoneId)}";

            using HttpClient http = new HttpClient();

            string response = await http.GetStringAsync(apiUrl);

            Console.WriteLine("API Response:");
            Console.WriteLine(response);

            using JsonDocument json = JsonDocument.Parse(response);

            if (!json.RootElement.TryGetProperty(
                "name",
                out JsonElement nameElement))
            {
                await command.FollowupAsync(
                    "❌ MLBB account not found.\nPlease check the MLBB ID and Server ID."
                );

                return;
            }

            string ign = nameElement.GetString() ?? "Unknown";

            ulong roleId = ulong.Parse(verifiedRoleId);

            SocketRole? role = guild.GetRole(roleId);

            if (role == null)
            {
                await command.FollowupAsync(
                    "❌ The Verified role could not be found."
                );

                return;
            }

            if (role.Position >= guild.CurrentUser.Hierarchy)
            {
                await command.FollowupAsync(
                    "❌ I cannot give the Verified role because my bot role is not above the Verified role."
                );

                return;
            }

            if (targetMember.Hierarchy >= guild.CurrentUser.Hierarchy)
            {
                await command.FollowupAsync(
                    "❌ I cannot modify this member because their highest role is equal to or higher than my bot role."
                );

                return;
            }

            await targetMember.AddRoleAsync(role);

            await targetMember.ModifyAsync(properties =>
            {
                properties.Nickname = ign;
            });

            await command.FollowupAsync(
                $"✅ Verification successful!\n\n" +
                $"Member: {targetMember.Mention}\n" +
                $"MLBB IGN: **{ign}**\n" +
                $"Verified role: **Added**\n" +
                $"Nickname: **{ign}**"
            );
        }
        catch (HttpRequestException)
        {
            await command.FollowupAsync(
                "❌ Unable to connect to the MLBB lookup service. Please try again later."
            );
        }
        catch (JsonException)
        {
            await command.FollowupAsync(
                "❌ The MLBB lookup service returned an invalid response."
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR:");
            Console.WriteLine(ex);

            await command.FollowupAsync(
                "❌ Something went wrong while verifying the member."
            );
        }
    }

    static Task Log(LogMessage message)
    {
        Console.WriteLine(message.ToString());

        return Task.CompletedTask;
    }
}