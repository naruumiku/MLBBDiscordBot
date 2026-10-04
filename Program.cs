using Discord;
using Discord.WebSocket;
using System.Text.Json;

class Program
{
    static DiscordSocketClient client = null!;

    static string discordServerId = "1311354688821792768";
    static string verifiedRoleId = "1311364927461527624";

    static string databaseFile = "verified_accounts.json";

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

        var verifyCommand = new SlashCommandBuilder()
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

        var unverifyCommand = new SlashCommandBuilder()
            .WithName("unverify")
            .WithDescription("Remove a member's Mobile Legends verification")
            .AddOption(
                new SlashCommandOptionBuilder()
                    .WithName("member")
                    .WithDescription("The member you want to unverify")
                    .WithType(ApplicationCommandOptionType.User)
                    .WithRequired(true));

        await guild.CreateApplicationCommandAsync(verifyCommand.Build());
        await guild.CreateApplicationCommandAsync(unverifyCommand.Build());

        Console.WriteLine("/verify command registered!");
        Console.WriteLine("/unverify command registered!");
    }

    static async Task HandleInteraction(SocketInteraction interaction)
    {
        if (interaction is SocketSlashCommand command)
        {
            if (command.Data.Name == "verify")
            {
                await VerifyMLBB(command);
            }

            if (command.Data.Name == "unverify")
            {
                await UnverifyMLBB(command);
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

            List<VerifiedAccount> accounts = LoadAccounts();

            VerifiedAccount? existingAccount = accounts.FirstOrDefault(
                x => x.MlbbId == mlbbId &&
                     x.ServerId == serverZoneId
            );

            if (existingAccount != null)
            {
                if (existingAccount.DiscordUserId == targetMember.Id.ToString())
                {
                    await command.FollowupAsync(
                        "❌ This member is already verified with this MLBB account."
                    );

                    return;
                }

                await command.FollowupAsync(
                    "❌ This MLBB account is already linked to another Discord member."
                );

                return;
            }

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

            VerifiedAccount newAccount = new VerifiedAccount
            {
                MlbbId = mlbbId,
                ServerId = serverZoneId,
                Ign = ign,
                DiscordUserId = targetMember.Id.ToString()
            };

            accounts.Add(newAccount);

            SaveAccounts(accounts);

            await command.FollowupAsync(
                $"✅ Verification successful!\n\n" +
                $"Member: {targetMember.Mention}\n" +
                $"MLBB IGN: **{ign}**\n" +
                $"Role Assigned: **Verified**"
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

    static async Task UnverifyMLBB(SocketSlashCommand command)
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

            List<VerifiedAccount> accounts = LoadAccounts();

            VerifiedAccount? existingAccount = accounts.FirstOrDefault(
                x => x.DiscordUserId == targetMember.Id.ToString()
            );

            if (existingAccount == null)
            {
                await command.FollowupAsync(
                    "❌ This member does not have a verified MLBB account."
                );

                return;
            }

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
                    "❌ I cannot remove the Verified role because my bot role is not above the Verified role."
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

            await targetMember.RemoveRoleAsync(role);

            await targetMember.ModifyAsync(properties =>
            {
                properties.Nickname = null;
            });

            accounts.Remove(existingAccount);

            SaveAccounts(accounts);

            await command.FollowupAsync(
                $"✅ Unverification successful!\n\n" +
                $"Member: {targetMember.Mention}\n" +
                $"MLBB IGN: **{existingAccount.Ign}**\n" +
                $"Binding: **Removed**\n" +
                $"Role Assigned: **Removed**"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR:");
            Console.WriteLine(ex);

            await command.FollowupAsync(
                "❌ Something went wrong while unverifying the member."
            );
        }
    }

    static List<VerifiedAccount> LoadAccounts()
    {
        if (!File.Exists(databaseFile))
        {
            return new List<VerifiedAccount>();
        }

        try
        {
            string json = File.ReadAllText(databaseFile);

            return JsonSerializer.Deserialize<List<VerifiedAccount>>(json)
                   ?? new List<VerifiedAccount>();
        }
        catch
        {
            return new List<VerifiedAccount>();
        }
    }

    static void SaveAccounts(List<VerifiedAccount> accounts)
    {
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(accounts, options);

        File.WriteAllText(databaseFile, json);
    }

    static Task Log(LogMessage message)
    {
        Console.WriteLine(message.ToString());

        return Task.CompletedTask;
    }
}

class VerifiedAccount
{
    public string MlbbId { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Ign { get; set; } = "";
    public string DiscordUserId { get; set; } = "";
}