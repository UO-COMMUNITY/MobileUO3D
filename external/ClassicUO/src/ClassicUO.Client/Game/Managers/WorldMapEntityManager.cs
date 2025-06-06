﻿// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Network;
using ClassicUO.Network.Encryption;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal class WMapEntity
    {
        public WMapEntity(uint serial)
        {
            Serial = serial;

            //var mob = World.Mobiles.Get(serial);

            //if (mob != null)
            //    GetName();
        }

        public bool IsGuild;
        public uint LastUpdate;
        public string Name;
        public readonly uint Serial;
        public int X, Y, HP, Map;

        //public string GetName()
        //{
        //    Entity e = World.Get(Serial);

        //    if (e != null && !e.IsDestroyed && !string.IsNullOrEmpty(e.Name) && Name != e.Name)
        //    {
        //        Name = e.Name;
        //    }

        //    return string.IsNullOrEmpty(Name) ? "<out of range>" : Name;
        //}
    }

    internal sealed class WorldMapEntityManager
    {
        private bool _ackReceived;
        private uint _lastUpdate, _lastPacketSend, _lastPacketRecv;
        private readonly List<WMapEntity> _toRemove = new List<WMapEntity>();
        private readonly World _world;

        public WorldMapEntityManager(World world) { _world = world; }

        public bool Enabled
        {
            get
            {
                return ((_world.ClientFeatures.Flags & CharacterListFlags.CLF_NEW_MOVEMENT_SYSTEM) == 0 || _ackReceived) &&
                        (NetClient.Socket.Encryption == null || NetClient.Socket.Encryption.EncryptionType == 0) &&
                        ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.WorldMapShowParty &&
                        UIManager.GetGump<WorldMapGump>() != null; // horrible, but works
            }
        }

        public readonly Dictionary<uint, WMapEntity> Entities = new Dictionary<uint, WMapEntity>();

        public void SetACKReceived()
        {
            _ackReceived = true;
        }

        public void SetEnable(bool v)
        {
            if ((_world.ClientFeatures.Flags & CharacterListFlags.CLF_NEW_MOVEMENT_SYSTEM) != 0 && !_ackReceived)
            {
                Log.Warn("Server support new movement system. Can't use the 0xF0 packet to query guild/party position");
                v = false;
            }
            else if (NetClient.Socket.Encryption?.EncryptionType != 0 && !_ackReceived)
            {
                Log.Warn("Server has encryption. Can't use the 0xF0 packet to query guild/party position");
                v = false;
            }

            if (v)
            {
                RequestServerPartyGuildInfo(true);
            }
        }

        public void AddOrUpdate
        (
            uint serial,
            int x,
            int y,
            int hp,
            int map,
            bool isguild,
            string name = null,
            bool from_packet = false
        )
        {
            if (from_packet)
            {
                _lastPacketRecv = Time.Ticks + 10000;
            }
            else if (_lastPacketRecv < Time.Ticks)
            {
                return;
            }

            if (!Enabled)
            {
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                Entity ent = _world.Get(serial);

                if (ent != null && !string.IsNullOrEmpty(ent.Name))
                {
                    name = ent.Name;
                }
            }

            if (!Entities.TryGetValue(serial, out WMapEntity entity) || entity == null)
            {
                entity = new WMapEntity(serial)
                {
                    X = x, Y = y, HP = hp, Map = map,
                    LastUpdate = Time.Ticks + 1000,
                    IsGuild = isguild,
                    Name = name
                };

                Entities[serial] = entity;
            }
            else
            {
                entity.X = x;
                entity.Y = y;
                entity.HP = hp;
                entity.Map = map;
                entity.IsGuild = isguild;
                entity.LastUpdate = Time.Ticks + 1000;

                if (string.IsNullOrEmpty(entity.Name) && !string.IsNullOrEmpty(name))
                {
                    entity.Name = name;
                }
            }
        }

        public void Remove(uint serial)
        {
            if (Entities.ContainsKey(serial))
            {
                Entities.Remove(serial);
            }
        }

        public void RemoveUnupdatedWEntity()
        {
            if (_lastUpdate > Time.Ticks)
            {
                return;
            }

            _lastUpdate = Time.Ticks + 1000;

            long ticks = Time.Ticks - 1000;

            foreach (WMapEntity entity in Entities.Values)
            {
                if (entity.LastUpdate < ticks)
                {
                    _toRemove.Add(entity);
                }
            }

            if (_toRemove.Count != 0)
            {
                foreach (WMapEntity entity in _toRemove)
                {
                    Entities.Remove(entity.Serial);
                }

                _toRemove.Clear();
            }
        }

        public WMapEntity GetEntity(uint serial)
        {
            Entities.TryGetValue(serial, out WMapEntity entity);

            return entity;
        }

        public void RequestServerPartyGuildInfo(bool force = false)
        {
            if (!force && !Enabled)
            {
                return;
            }

            if (_world.InGame && _lastPacketSend < Time.Ticks)
            {
                //GameActions.Print($"SENDING PACKET! {Time.Ticks}");

                _lastPacketSend = Time.Ticks + 250;

                //if (!force && !_can_send)
                //{
                //    return;
                //}

                NetClient.Socket.Send_QueryGuildPosition();

                if (_world.Party != null && _world.Party.Leader != 0)
                {
                    foreach (PartyMember e in _world.Party.Members)
                    {
                        if (e != null && SerialHelper.IsValid(e.Serial))
                        {
                            Mobile mob = _world.Mobiles.Get(e.Serial);

                            if (mob == null || mob.Distance > _world.ClientViewRange)
                            {
                                NetClient.Socket.Send_QueryPartyPosition();

                                break;
                            }
                        }
                    }
                }
            }
        }

        public void Clear()
        {
            Entities.Clear();
            _ackReceived = false;
            SetEnable(false);
        }
    }
}