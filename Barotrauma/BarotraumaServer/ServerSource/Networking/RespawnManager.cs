﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class RespawnManager : Entity, IServerSerializable
    {
        /// <summary>
        /// How much skills drop towards the job's default skill levels when respawning midround in the campaign
        /// </summary>
        const float SkillReductionOnCampaignMidroundRespawn = 0.75f;

        private DateTime despawnTime;

        private float shuttleEmptyTimer;

        private int pendingRespawnCount, requiredRespawnCount;
        private int prevPendingRespawnCount, prevRequiredRespawnCount;

        private IEnumerable<Client> GetClientsToRespawn()
        {
            MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
            foreach (Client c in networkMember.ConnectedClients)
            {
                if (GameMain.LuaCs.Game.overrideRespawnSub)
                    continue;

                if (!c.InGame) { continue; }
                if (c.SpectateOnly && (GameMain.Server.ServerSettings.AllowSpectating || GameMain.Server.OwnerConnection == c.Connection)) { continue; }
                if (c.Character != null && !c.Character.IsDead) { continue; }

                //don't allow respawn if the client already has a character (they'll regain control once they're in sync)
                var matchingData = campaign?.GetClientCharacterData(c);
                if (matchingData != null && matchingData.HasSpawned &&
                    Character.CharacterList.Any(c => c.Info == matchingData.CharacterInfo && !c.IsDead))
                {
                    continue;
                }

                if (UseRespawnPrompt)
                {
                    if (matchingData != null && matchingData.HasSpawned)
                    {
                        if (!c.WaitForNextRoundRespawn.HasValue || c.WaitForNextRoundRespawn.Value) { continue; }
                    }
                }

                yield return c;
            }
        }

        private bool IsRespawnPromptPendingForClient(Client c)
        {
            if (!UseRespawnPrompt || !(GameMain.GameSession.GameMode is MultiPlayerCampaign campaign)) { return false; }

            if (!c.InGame) { return false; }
            if (c.SpectateOnly && (GameMain.Server.ServerSettings.AllowSpectating || GameMain.Server.OwnerConnection == c.Connection)) { return false; }
            if (c.Character != null && !c.Character.IsDead) { return false; }

            var matchingData = campaign.GetClientCharacterData(c);
            if (matchingData != null && matchingData.HasSpawned)
            {
                if (Character.CharacterList.Any(c => c.Info == matchingData.CharacterInfo && !c.IsDead))
                {
                    return false;
                }
                else if (!c.WaitForNextRoundRespawn.HasValue)
                {
                    return true;
                }
            }
            return false;
        }

        private List<CharacterInfo> GetBotsToRespawn()
        {
            if (GameMain.Server.ServerSettings.BotSpawnMode == BotSpawnMode.Normal)
            {
                return Character.CharacterList
                    .FindAll(c => c.TeamID == CharacterTeamType.Team1 && c.AIController != null && c.Info != null && c.IsDead)
                    .Select(c => c.Info)
                    .ToList();
            }

            int currPlayerCount = GameMain.Server.ConnectedClients.Count(c =>
                c.InGame &&
                (!c.SpectateOnly || (!GameMain.Server.ServerSettings.AllowSpectating && GameMain.Server.OwnerConnection != c.Connection)));

            var existingBots = Character.CharacterList
                .FindAll(c => c.TeamID == CharacterTeamType.Team1 && c.AIController != null && c.Info != null);

            int requiredBots = GameMain.Server.ServerSettings.BotCount - currPlayerCount;
            requiredBots -= existingBots.Count(b => !b.IsDead);

            List<CharacterInfo> botsToRespawn = new List<CharacterInfo>();
            for (int i = 0; i < requiredBots; i++)
            {
                CharacterInfo botToRespawn = existingBots.Find(b => b.IsDead)?.Info;
                if (botToRespawn == null)
                {
                    botToRespawn = new CharacterInfo(CharacterPrefab.HumanSpeciesName);
                }
                else
                {
                    existingBots.Remove(botToRespawn.Character);
                }
                botsToRespawn.Add(botToRespawn);
            }
            return botsToRespawn;
        }

        private bool ShouldStartRespawnCountdown()
        {
            int characterToRespawnCount = GetClientsToRespawn().Count();
            return ShouldStartRespawnCountdown(characterToRespawnCount);
        }

        private int GetMinCharactersToRespawn()
        {
            return Math.Max((int)(GameMain.Server.ConnectedClients.Count * GameMain.Server.ServerSettings.MinRespawnRatio), 1);
        }

        private bool ShouldStartRespawnCountdown(int characterToRespawnCount)
        {
            if (GameMain.LuaCs.Game.overrideRespawnSub)
            {
                characterToRespawnCount = 0;
            }
            return characterToRespawnCount >= GetMinCharactersToRespawn();
        }

        partial void UpdateWaiting(float deltaTime)
        {
            if (RespawnShuttle != null)
            {
                if (!GameMain.LuaCs.Game.overrideRespawnSub)
                {
                    RespawnShuttle.Velocity = Vector2.Zero;
                }
            }

            pendingRespawnCount = GetClientsToRespawn().Count();
            requiredRespawnCount = GetMinCharactersToRespawn();
            if (pendingRespawnCount != prevPendingRespawnCount || 
                requiredRespawnCount != prevRequiredRespawnCount)
            {
                prevPendingRespawnCount = pendingRespawnCount;
                prevRequiredRespawnCount = requiredRespawnCount;
                GameMain.Server.CreateEntityEvent(this);
            }

            if (RespawnCountdownStarted)
            {
                if (pendingRespawnCount == 0)
                {
                    RespawnCountdownStarted = false;
                    GameMain.Server.CreateEntityEvent(this);
                }
            }
            else
            {
                bool shouldStartCountdown = ShouldStartRespawnCountdown(pendingRespawnCount);       
                if (shouldStartCountdown)
                {
                    RespawnCountdownStarted = true;
                    if (RespawnTime < DateTime.Now)
                    {
                        RespawnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, (int)(GameMain.Server.ServerSettings.RespawnInterval * 1000.0f));
                    }
                    GameMain.Server.CreateEntityEvent(this); 
                }              
            }

            if (RespawnCountdownStarted && DateTime.Now > RespawnTime)
            {
                DispatchShuttle();
                RespawnCountdownStarted = false;
            }
        }

        public void DispatchShuttle()
        {
            if (RespawnShuttle != null)
            {
                if (GameMain.LuaCs.Game.overrideRespawnSub)
				{
                    CurrentState = State.Waiting;
                }
                else
				{
                    CurrentState = State.Transporting;
                }
                GameMain.Server.CreateEntityEvent(this);

                ResetShuttle();

                if (shuttleSteering != null)
                {
                    shuttleSteering.TargetVelocity = Vector2.Zero;
                }

                GameServer.Log("Dispatching the respawn shuttle.", ServerLog.MessageType.Spawning);

                Vector2 spawnPos = FindSpawnPos();

                if (!GameMain.LuaCs.Game.overrideRespawnSub)
                {
                    RespawnCharacters(spawnPos);
                }

                CoroutineManager.StopCoroutines("forcepos");
                if (spawnPos.Y > Level.Loaded.Size.Y)
                {
                    CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition - Vector2.UnitY * Level.ShaftHeight, 100.0f), "forcepos");
                }
                else
                {
                    RespawnShuttle.SetPosition(spawnPos);
                    RespawnShuttle.Velocity = Vector2.Zero;
                    RespawnShuttle.NeutralizeBallast();
                    RespawnShuttle.EnableMaintainPosition();
                }
            }
            else
            {
                CurrentState = State.Waiting;
                GameServer.Log("Respawning everyone in main sub.", ServerLog.MessageType.Spawning);
                GameMain.Server.CreateEntityEvent(this);

                RespawnCharacters(null);
            }
        }

        partial void UpdateReturningProjSpecific(float deltaTime)
        {
            //speed up despawning if there's no-one inside the shuttle
            if (despawnTime > DateTime.Now + new TimeSpan(0, 0, seconds: 30) && CheckShuttleEmpty(deltaTime))
            {
                despawnTime = DateTime.Now + new TimeSpan(0, 0, seconds: 30);
            }

            foreach (Door door in shuttleDoors)
            {
                if (door.IsOpen) door.TrySetState(false, false, true);
            }

            var shuttleGaps = Gap.GapList.FindAll(g => g.Submarine == RespawnShuttle && g.ConnectedWall != null);
            shuttleGaps.ForEach(g => Spawner.AddEntityToRemoveQueue(g));

            var dockingPorts = Item.ItemList.FindAll(i => i.Submarine == RespawnShuttle && i.GetComponent<DockingPort>() != null);
            dockingPorts.ForEach(d => d.GetComponent<DockingPort>().Undock());

            //shuttle has returned if the path has been traversed or the shuttle is close enough to the exit
            if (!CoroutineManager.IsCoroutineRunning("forcepos"))
            {
                if ((shuttleSteering?.SteeringPath != null && shuttleSteering.SteeringPath.Finished)
                    || (RespawnShuttle.WorldPosition.Y + RespawnShuttle.Borders.Y > Level.Loaded.StartPosition.Y - Level.ShaftHeight &&
                        Math.Abs(Level.Loaded.StartPosition.X - RespawnShuttle.WorldPosition.X) < 1000.0f))
                {
                    CoroutineManager.StopCoroutines("forcepos");
                    CoroutineManager.StartCoroutine(
                        ForceShuttleToPos(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + 1000.0f), 100.0f), "forcepos");

                }
            }

            if (RespawnShuttle.WorldPosition.Y > Level.Loaded.Size.Y || DateTime.Now > despawnTime)
            {
                CoroutineManager.StopCoroutines("forcepos");

                ResetShuttle();

                CurrentState = State.Waiting;
                GameServer.Log("The respawn shuttle has left.", ServerLog.MessageType.Spawning);
                GameMain.Server.CreateEntityEvent(this);

                RespawnCountdownStarted = false;
                ReturnCountdownStarted = false;
            }
        }

        partial void UpdateTransportingProjSpecific(float deltaTime)
        {
            if (!ReturnCountdownStarted)
            {
                //if there are no living chracters inside, transporting can be stopped immediately
                if (CheckShuttleEmpty(deltaTime))
                {
                    ReturnTime = DateTime.Now;
                    ReturnCountdownStarted = true;
                }
                else if (!ShouldStartRespawnCountdown())
                {
                    //don't start counting down until someone else needs to respawn
                    ReturnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(maxTransportTime * 1000));
                    despawnTime = ReturnTime + new TimeSpan(0, 0, seconds: 30);
                    return;
                }
                else
                {
                    ReturnCountdownStarted = true;
                    GameMain.Server.CreateEntityEvent(this);
                }
            }
            else if (CheckShuttleEmpty(deltaTime))
            {
                ReturnTime = DateTime.Now;
            }

            if (DateTime.Now > ReturnTime)
            {
                GameServer.Log("The respawn shuttle is leaving.", ServerLog.MessageType.ServerMessage);
                CurrentState = State.Returning;

                GameMain.Server.CreateEntityEvent(this);

                RespawnCountdownStarted = false;
                maxTransportTime = GameMain.Server.ServerSettings.MaxTransportTime;
            }
        }

        private bool CheckShuttleEmpty(float deltaTime)
        {
            if (!Character.CharacterList.Any(c => c.Submarine == RespawnShuttle && !c.IsDead))
            {
                shuttleEmptyTimer += deltaTime;
            }
            else
            {
                shuttleEmptyTimer = 0.0f;
            }
            return shuttleEmptyTimer > 1.0f;
        }
        
        partial void RespawnCharactersProjSpecific(Vector2? shuttlePos)
        {
            respawnedCharacters.Clear();

            var respawnSub = RespawnShuttle ?? Submarine.MainSub;

            MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;

            var clients = GetClientsToRespawn().ToList();
            foreach (Client c in clients)
            {
                //get rid of the existing character
                c.Character?.DespawnNow();

                c.WaitForNextRoundRespawn = null;

                var matchingData = campaign?.GetClientCharacterData(c);
                if (matchingData != null)
                {
                    c.CharacterInfo = matchingData.CharacterInfo;
                }

                //all characters are in Team 1 in game modes/missions with only one team.
                //if at some point we add a game mode with multiple teams where respawning is possible, this needs to be reworked
                c.TeamID = CharacterTeamType.Team1;
                if (c.CharacterInfo == null) { c.CharacterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, c.Name); }
            }
            List<CharacterInfo> characterInfos = clients.Select(c => c.CharacterInfo).ToList();

            //bots don't respawn in the campaign
            if (campaign == null)
            {
                var botsToSpawn = GetBotsToRespawn();
                characterInfos.AddRange(botsToSpawn);
            }

            GameMain.Server.AssignJobs(clients);
            foreach (Client c in clients)
            {
                if (campaign?.GetClientCharacterData(c) == null || c.CharacterInfo.Job == null)
                {
                    c.CharacterInfo.Job = new Job(c.AssignedJob.Prefab, Rand.RandSync.Unsynced, c.AssignedJob.Variant);
                }
            }

            //the spawnpoints where the characters will spawn
            var shuttleSpawnPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, respawnSub);
            //the spawnpoints where they would spawn if they were spawned inside the main sub
            //(in order to give them appropriate ID card tags)
            var mainSubSpawnPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);

            ItemPrefab divingSuitPrefab = null;
            if ((shuttlePos != null && Level.Loaded.GetRealWorldDepth(shuttlePos.Value.Y) > Level.DefaultRealWorldCrushDepth) ||
                Level.Loaded.GetRealWorldDepth(Submarine.MainSub.WorldPosition.Y) > Level.DefaultRealWorldCrushDepth)
            {
                divingSuitPrefab = ItemPrefab.Prefabs.FirstOrDefault(it => it.Tags.Any(t => t == "respawnsuitdeep"));
            }
            if (divingSuitPrefab == null)
            {
                divingSuitPrefab = 
                    ItemPrefab.Prefabs.FirstOrDefault(it => it.Tags.Any(t => t == "respawnsuit")) ??
                    ItemPrefab.Find(null, "divingsuit".ToIdentifier());
            }
            ItemPrefab oxyPrefab = ItemPrefab.Find(null, "oxygentank".ToIdentifier());
            ItemPrefab scooterPrefab = ItemPrefab.Find(null, "underwaterscooter".ToIdentifier());
            ItemPrefab batteryPrefab = ItemPrefab.Find(null, "batterycell".ToIdentifier());

            var cargoSp = WayPoint.WayPointList.Find(wp => wp.Submarine == respawnSub && wp.SpawnType == SpawnType.Cargo);

            for (int i = 0; i < characterInfos.Count; i++)
            {
                bool bot = i >= clients.Count;

                characterInfos[i].ClearCurrentOrders();

                bool forceSpawnInMainSub = false;
                if (!bot && campaign != null)
                {
                    var matchingData = campaign?.GetClientCharacterData(clients[i]);
                    if (matchingData != null)
                    {
                        if (!matchingData.HasSpawned)
                        {
                            forceSpawnInMainSub = true;
                        }
                        else
                        {
                            ReduceCharacterSkills(characterInfos[i]);
                            characterInfos[i].RemoveSavedStatValuesOnDeath();
                            characterInfos[i].CauseOfDeath = null;
                        }
                    }
                }

                var character = Character.Create(characterInfos[i], (forceSpawnInMainSub ? mainSubSpawnPoints[i] : shuttleSpawnPoints[i]).WorldPosition, characterInfos[i].Name, isRemotePlayer: !bot, hasAi: bot);
                character.TeamID = CharacterTeamType.Team1;
                character.LoadTalents();

                respawnedCharacters.Add(character);

                if (bot)
                {
                    GameServer.Log(string.Format("Respawning bot {0} as {1}", character.Info.Name, characterInfos[i].Job.Name), ServerLog.MessageType.Spawning);
                }
                else
                {
                    if (GameMain.GameSession?.GameMode is MultiPlayerCampaign mpCampaign && character.Info != null)
                    {
                        character.Info.SetExperience(Math.Max(character.Info.ExperiencePoints, mpCampaign.GetSavedExperiencePoints(clients[i])));
                        mpCampaign.ClearSavedExperiencePoints(clients[i]);
                    }

                    //tell the respawning client they're no longer a traitor
                    if (GameMain.Server.TraitorManager?.Traitors != null && clients[i].Character != null)
                    {
                        if (GameMain.Server.TraitorManager.Traitors.Any(t => t.Character == clients[i].Character))
                        {
                            GameMain.Server.SendDirectChatMessage(TextManager.FormatServerMessage("TraitorRespawnMessage"), clients[i], ChatMessageType.ServerMessageBox);
                        }
                    }

                    clients[i].Character = character;
                    character.OwnerClientEndPoint = clients[i].Connection.EndPointString;
                    character.OwnerClientName = clients[i].Name;
                    GameServer.Log(string.Format("Respawning {0} ({1}) as {2}", GameServer.ClientLogName(clients[i]), clients[i].Connection?.EndPointString, characterInfos[i].Job.Name), ServerLog.MessageType.Spawning);
                }

                if (divingSuitPrefab != null && oxyPrefab != null && RespawnShuttle != null)
                {
                    Vector2 pos = cargoSp == null ? character.Position : cargoSp.Position;
                    if (divingSuitPrefab != null && oxyPrefab != null)
                    {
                        var divingSuit = new Item(divingSuitPrefab, pos, respawnSub);
                        Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(divingSuit));
                        respawnItems.Add(divingSuit);

                        var oxyTank = new Item(oxyPrefab, pos, respawnSub);
                        Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(oxyTank));
                        divingSuit.Combine(oxyTank, user: null);
                        respawnItems.Add(oxyTank);
                    }

                    if (scooterPrefab != null && batteryPrefab != null)
                    {
                        var scooter = new Item(scooterPrefab, pos, respawnSub);
                        Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(scooter));

                        var battery = new Item(batteryPrefab, pos, respawnSub);
                        Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(battery));

                        scooter.Combine(battery, user: null);
                        respawnItems.Add(scooter);
                        respawnItems.Add(battery);
                    }
                }

                var characterData = campaign?.GetClientCharacterData(clients[i]);
                if (characterData != null && Level.Loaded?.Type != LevelData.LevelType.Outpost && characterData.HasSpawned)
                {
                    GiveRespawnPenaltyAffliction(character);
                }

                if (characterData == null || characterData.HasSpawned)
                {
                    //give the character the items they would've gotten if they had spawned in the main sub
                    character.GiveJobItems(mainSubSpawnPoints[i]);
                    if (campaign != null)
                    {
                        characterData = campaign.SetClientCharacterData(clients[i]);
                        characterData.HasSpawned = true;
                    }
                }
                else
                {
                    if (characterData.HasItemData)
                    {
                        characterData.SpawnInventoryItems(character, character.Inventory);
                    }
                    else
                    {
                        character.GiveJobItems(mainSubSpawnPoints[i]);
                    }
                    characterData.ApplyHealthData(character);
                    character.GiveIdCardTags(mainSubSpawnPoints[i]);
                    characterData.HasSpawned = true;
                }

                //add the ID card tags they should've gotten when spawning in the shuttle
                foreach (Item item in character.Inventory.AllItems.Distinct())
                {
                    if (item.Prefab.Identifier != "idcard") { continue; }
                    foreach (string s in shuttleSpawnPoints[i].IdCardTags)
                    {
                        item.AddTag(s);
                    }
                    if (!string.IsNullOrWhiteSpace(shuttleSpawnPoints[i].IdCardDesc))
                    {
                        item.Description = shuttleSpawnPoints[i].IdCardDesc;
                    }
                }
            }
        }

        public static void ReduceCharacterSkills(CharacterInfo characterInfo)
        {
            if (characterInfo?.Job == null) { return; }
            foreach (Skill skill in characterInfo.Job.Skills)
            {
                var skillPrefab = characterInfo.Job.Prefab.Skills.Find(s => skill.Identifier == s.Identifier);
                if (skillPrefab == null) { continue; }
                skill.Level = MathHelper.Lerp(skill.Level, skillPrefab.LevelRange.Start, SkillReductionOnCampaignMidroundRespawn);
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteRangedInteger((int)CurrentState, 0, Enum.GetNames(typeof(State)).Length);

            switch (CurrentState)
            {
                case State.Transporting:
                    msg.Write(ReturnCountdownStarted);
                    msg.Write(GameMain.Server.ServerSettings.MaxTransportTime);
                    msg.Write((float)(ReturnTime - DateTime.Now).TotalSeconds);
                    break;
                case State.Waiting:
                    MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
                    var matchingData = campaign?.GetClientCharacterData(c);
                    bool forceSpawnInMainSub = matchingData != null && !matchingData.HasSpawned;
                    msg.Write((ushort)pendingRespawnCount);
                    msg.Write((ushort)requiredRespawnCount);
                    msg.Write(IsRespawnPromptPendingForClient(c));
                    msg.Write(RespawnCountdownStarted);
                    msg.Write(forceSpawnInMainSub);
                    msg.Write((float)(RespawnTime - DateTime.Now).TotalSeconds);
                    break;
                case State.Returning:
                    break;
            }

            msg.WritePadBits();
        }
    }
}
