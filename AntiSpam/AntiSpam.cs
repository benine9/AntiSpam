using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace AntiSpam
{
    [ApiVersion(2, 1)]
    public class AntiSpam : TerrariaPlugin
    {
        private Config Config = new();
        private DateTime[] Times = new DateTime[256];
        private double[] Spams = new double[256];

        public override string Author => "MarioE";
        public override string Description => "Prevents spamming.";
        public override string Name => "AntiSpam";
        public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;

        public AntiSpam(Main game) : base(game) => Order = 1000000;

        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.NetSendData.Register(this, OnSendData);
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            PlayerHooks.PlayerCommand += OnPlayerCommand;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.NetSendData.Deregister(this, OnSendData);
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                PlayerHooks.PlayerCommand -= OnPlayerCommand;
            }
            base.Dispose(disposing);
        }

        private void OnChat(ServerChatEventArgs e)
        {
            if (!e.Handled)
            {
                string text = e.Text;
                if (IsCommandPrefix(text))
                    return;

                int playerIndex = e.Who;
                UpdateSpamMetrics(playerIndex);
                double spamScore = CalculateSpamScore(text);

                if (spamScore > Config.Threshold && !CanBypassSpam(playerIndex))
                {
                    HandleSpamAction(playerIndex);
                    e.Handled = true;
                }
            }
        }

        private void OnPlayerCommand(PlayerCommandEventArgs e)
        {
            if (!e.Handled && e.Player.RealPlayer)
            {
                string commandName = e.CommandName.ToLower();
                int playerIndex = e.Player.Index;

                if (IsCommandWhisper(commandName))
                {
                    string text = e.CommandText.Substring(e.CommandName.Length);
                    UpdateSpamMetrics(playerIndex);
                    double spamScore = CalculateSpamScore(text);

                    if (spamScore > Config.Threshold && !CanBypassSpam(playerIndex))
                    {
                        HandleSpamAction(playerIndex);
                        e.Handled = true;
                    }
                }
            }
        }

        private void OnInitialize(EventArgs e)
        {
            Commands.ChatCommands.Add(new Command("antispam.reload", Reload, "asreload"));

            string configPath = Path.Combine(TShock.SavePath, "antispamconfig.json");
            if (File.Exists(configPath))
                Config = Config.Read(configPath);
            Config.Write(configPath);
        }

        private void OnLeave(LeaveEventArgs e) => ResetSpamMetrics(e.Who);

        private void OnSendData(SendDataEventArgs e)
        {
            if (ShouldBlockBossMessages(e) || ShouldBlockOrbMessages(e))
                e.Handled = true;
        }

        private void Reload(CommandArgs e)
        {
            string configPath = Path.Combine(TShock.SavePath, "antispamconfig.json");
            if (File.Exists(configPath))
                Config = Config.Read(configPath);
            Config.Write(configPath);
            e.Player.SendSuccessMessage("Reloaded antispam config.");
        }

        private bool IsCommandPrefix(string text) => text.StartsWith(Commands.Specifier) || text.StartsWith(Commands.SilentSpecifier);

        private bool IsCommandWhisper(string commandName)
        {
            string[] whisperCommands = { "me", "r", "reply", "tell", "w", "whisper" };
            return whisperCommands.Contains(commandName);
        }

        private void UpdateSpamMetrics(int playerIndex)
        {
            if ((DateTime.Now - Times[playerIndex]).TotalSeconds > Config.Time)
            {
                Spams[playerIndex] = 0.0;
                Times[playerIndex] = DateTime.Now;
            }
        }

        private double CalculateSpamScore(string text)
        {
            if (text.Trim().Length <= Config.ShortLength)
                return Spams[0] + Config.ShortWeight;
            double capsRatio = (double)text.Count(c => char.IsUpper(c)) / text.Length;
            if (capsRatio >= Config.CapsRatio)
                return Spams[0] + Config.CapsWeight;
            return Spams[0] + Config.NormalWeight;
        }

        public static bool CanBypassSpam(int playerIndex) => TShock.Players[playerIndex].Group.HasPermission("antispam.ignore");

        private void HandleSpamAction(int playerIndex)
        {
            switch (Config.Action.ToLower())
            {
                case "ignore":
                default:
                    Times[playerIndex] = DateTime.Now;
                    TShock.Players[playerIndex].SendErrorMessage("You have been ignored for spamming.");
                    break;
                case "kick":
                    TShock.Players[playerIndex].Kick("Spamming");
                    break;
            }
        }

        private bool ShouldBlockBossMessages(SendDataEventArgs e)
        {
            if (Config.DisableBossMessages && e.MsgId == PacketTypes.SmartTextMessage && e.number2 == 175 && e.number3 == 75 && e.number4 == 255)
            {
                string text = e.text._text;
                string[] bossNames = { "Eye of Cthulhu", "Eater of Worlds", "Skeletron", "King Slime", "The Destroyer", "The Twins", "Skeletron Prime", "Wall of Flesh", "Plantera", "Golem", "Brain of Cthulhu", "Queen Bee", "Duke Fishron" };
                return bossNames.Any(boss => text.StartsWith(boss));
            }
            return false;
        }

        private bool ShouldBlockOrbMessages(SendDataEventArgs e)
        {
            if (Config.DisableOrbMessages && e.MsgId == PacketTypes.SmartTextMessage && e.number2 == 50 && e.number3 == 255 && e.number4 == 130)
            {
                string text = e.text._text;
                return text == "A horrible chill goes down your spine..." || text == "Screams echo around you...";
            }
            return false;
        }

        private void ResetSpamMetrics(int playerIndex)
        {
            Spams[playerIndex] = 0.0;
            Times[playerIndex] = DateTime.Now;
        }
    }
}
