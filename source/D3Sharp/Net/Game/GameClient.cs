﻿/*
 * Copyright (C) 2011 D3Sharp Project
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
 */
using System;
using System.IO;
using System.Linq;
using D3Sharp.Net.BNet;
using D3Sharp.Utils.Extensions;
using Gibbed.Helpers;
using System.Text;
using D3Sharp.Utils;
using System.Collections.Generic;

//using Gibbed.Helpers;

namespace D3Sharp.Net.Game
{
    public class Mob
    {
        public Vector3D pos;
        public int id;
    }

    public sealed class GameClient : IGameClient, IGameMessageHandler
    {
        static readonly Logger Logger = LogManager.CreateLogger();
        static public int WorldID = 0x772E0000;

        public IConnection Connection { get; set; }
        public BNetClient BnetClient { get; private set; }

        GameBitBuffer _incomingBuffer = new GameBitBuffer(512);
        GameBitBuffer _outgoingBuffer = new GameBitBuffer(ushort.MaxValue);

        public float posx, posy, posz;
        int objectId = 0x78f50114 + 100;
        int packetId = 0x227 + 20;
        int tick = 1;
        int[] mobs = { 5346, 5347, 5350, 5360, 5361, 5362, 5363, 5365, 5387, 5393, 5395, 5397, 5411, 5428, 5432, 5433, 5467 };

        Random rand = new Random();
        IList<Mob> objectsSpawned = null;
        Vector3D position;

        public GameClient(IConnection connection)
        {
            this.Connection = connection;
            _outgoingBuffer.WriteInt(32, 0);
        }
        public void Parse(ConnectionDataEventArgs e)
        {
            //Console.WriteLine(e.Data.Dump());

            _incomingBuffer.AppendData(e.Data.ToArray());

            while (_incomingBuffer.IsPacketAvailable())
            {
                int end = _incomingBuffer.Position;
                end += _incomingBuffer.ReadInt(32) * 8;

                while ((end - _incomingBuffer.Position) >= 9)
                {
                    GameMessage msg = _incomingBuffer.ParseMessage();

                    Logger.LogIncoming(msg);

                    try
                    {
                        msg.VisitHandler(this);
                    }
                    catch (NotImplementedException)
                    {
                        Logger.Debug("Unhandled game message: 0x{0:X4} {1}", msg.Id, msg.GetType().Name);
                    }
                }

                _incomingBuffer.Position = end;
            }
            _incomingBuffer.ConsumeData();
            FlushOutgoingBuffer();
        }
        public void SendMessage(GameMessage msg)
        {
            //Logger.LogOutgoing(msg);
            _outgoingBuffer.EncodeMessage(msg);
        }
        public void FlushOutgoingBuffer()
        {
            if (_outgoingBuffer.Length > 32)
            {
                var data = _outgoingBuffer.GetPacketAndReset();
                Connection.Send(data);
            }
        }

        public void ReadAndSendMap()
        {
            string filePath = Config.Instance.Map;
            string line, line2;

            //avarage = double.Parse("0.0", NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);

            bool versiondetermined = false;
            int version = 0;

            if (File.Exists(filePath))
            {
                StreamReader file = null;
                try
                {

                    System.Text.RegularExpressions.Regex rx = new System.Text.RegularExpressions.Regex(@"\s+");

                    file = new StreamReader(filePath);
                    while ((line = file.ReadLine()) != null)
                    {
                        line = rx.Replace(line, @" ");
                        string[] data = line.Split(' ');

                        if (!versiondetermined)
                            if (data.Length > 0)
                                if (data[0].Equals("v"))
                                {
                                    version = int.Parse(data[1]);
                                    Logger.Info("Map file version: " + version);
                                }
                                else
                                {
                                    //reveal world here if fallback - updated map files have world reveal data
                                    #region Interstitial,RevealWorld,WorldStatus,EnterWorld
                                    SendMessage(new RevealWorldMessage()
                                    {
                                        Id = 0x0037,
                                        Field0 = WorldID,
                                        Field1 = 0x000115EE,
                                    });

                                    SendMessage(new EnterWorldMessage()
                                    {
                                        Id = 0x0033,
                                        Field0 = new Vector3D() { Field0 = 3143.75f, Field1 = 2828.75f, Field2 = 59.07559f },
                                        Field1 = WorldID,
                                        Field2 = 0x000115EE,
                                    });
                                    #endregion
                                }
                        versiondetermined = true;

                        if (version == 0)
                        {
                            //fallback to the original version of the text files because people WILL mix them with the new :(

                            if ((line2 = file.ReadLine()) != null)
                            {
                                string[] data2 = line2.Split(' ');

                                RevealSceneMessage r = new RevealSceneMessage(data, WorldID);
                                MapRevealSceneMessage r2 = new MapRevealSceneMessage(data2, WorldID);

                                posx = (r.Field1.tCachedValues.Field3.Field0.Field0 + r.Field1.tCachedValues.Field3.Field1.Field0) / 2.0f + r.Field4.Field1.Field0;
                                posy = (r.Field1.tCachedValues.Field3.Field0.Field1 + r.Field1.tCachedValues.Field3.Field1.Field1) / 2.0f + r.Field4.Field1.Field1;
                                posz = (r.Field1.tCachedValues.Field3.Field0.Field2 + r.Field1.tCachedValues.Field3.Field1.Field2) / 2.0f + r.Field4.Field1.Field2;

                                SendMessage(r);
                                SendMessage(r2);
                            }
                        }
                        else
                            if (data.Length >= 1) //check only lines with data in them
                            {
                                //packet data
                                if (data[0].Equals("p") && data.Length >= 2)
                                {
                                    int packettype = int.Parse(data[1]);
                                    switch (packettype)
                                    {
                                        case 0x34: //revealscenemessage
                                            SendMessage(new RevealSceneMessage(data.Skip(2).ToArray(), WorldID));
                                            break;
                                        case 0x33: //enterworldmessage
                                            WorldID = int.Parse(data[5]);
                                            SendMessage(new EnterWorldMessage()
                                            {
                                                Id = 0x0033,
                                                Field0 = new Vector3D()
                                                {
                                                    Field0 = float.Parse(data[2], System.Globalization.CultureInfo.InvariantCulture),
                                                    Field1 = float.Parse(data[3], System.Globalization.CultureInfo.InvariantCulture),
                                                    Field2 = float.Parse(data[4], System.Globalization.CultureInfo.InvariantCulture)
                                                },
                                                Field1 = WorldID,
                                                Field2 = int.Parse(data[6]),
                                            });
                                            break;
                                        case 0x37: //revealworldmessage
                                            SendMessage(new RevealWorldMessage()
                                            {
                                                Id = 0x0037,
                                                Field0 = int.Parse(data[2]),
                                                Field1 = int.Parse(data[3]),
                                            });
                                            break;
                                        case 0x3b: //acdenterknownmessage
                                            //if (int.Parse(data[5]) == 1)
                                            {
                                                SendMessage(new ACDEnterKnownMessage(data.Skip(2).ToArray(), WorldID));
                                                //posx = float.Parse(data[11], System.Globalization.CultureInfo.InvariantCulture);
                                                //posy = float.Parse(data[12], System.Globalization.CultureInfo.InvariantCulture);
                                                //posz = float.Parse(data[13], System.Globalization.CultureInfo.InvariantCulture);
                                            }
                                            break;
                                        case 0x44: //maprevealscenemessage
                                            SendMessage(new MapRevealSceneMessage(data.Skip(2).ToArray(), WorldID));
                                            break;
                                        default:
                                            Logger.Error("Unimplemented packet type encountered in map file: " + packettype);
                                            break;
                                    }
                                }

                                //spawn point
                                if (data[0].Equals("s") && data.Length >= 4)
                                {
                                    posx = float.Parse(data[1], System.Globalization.CultureInfo.InvariantCulture);
                                    posy = float.Parse(data[2], System.Globalization.CultureInfo.InvariantCulture);
                                    posz = float.Parse(data[3], System.Globalization.CultureInfo.InvariantCulture);
                                }

                            }

                    }
                }
                catch (Exception e)
                {
                    Logger.DebugException(e, "ReadAndSendMap");
                }
                finally
                {
                    if (file != null)
                        file.Close();
                }
            }
            else
            {
                Logger.Error("Map file {0} not found!", filePath);
            }
        }
        public void OnMessage(JoinBNetGameMessage msg)
        {
            if (msg.Id != 0x000A)
                throw new NotImplementedException();

            // a hackish way to get bnetclient in context -- pretends games has only one client in. when we're done with implementing bnet completely, will get this sorted out. /raist
            this.BnetClient = Core.Games.GameManager.AvailableGames[(ulong)msg.Field2].Clients.FirstOrDefault();
            if (this.BnetClient != null) this.BnetClient.InGameClient = this;

            SendMessage(new VersionsMessage()
            {
                Id = 0x000D,
                SNOPackHash = msg.SNOPackHash,
                ProtocolHash = GameMessage.ImplementedProtocolHash,
                Version = "0.3.0.7333",
            });
            FlushOutgoingBuffer();

            SendMessage(new ConnectionEstablishedMessage()
            {
                Id = 0x002E,
                Field0 = 0x00000000,
                Field1 = 0x4BB91A16,
                Field2 = msg.SNOPackHash,
            });
            SendMessage(new GameSetupMessage()
            {
                Id = 0x002F,
                Field0 = 0x00000077,
            });

            SendMessage(new SavePointInfoMessage()
            {
                Id = 0x0045,
                snoLevelArea = -1,
            });

            SendMessage(new HearthPortalInfoMessage()
            {
                Id = 0x0046,
                snoLevelArea = -1,
                Field1 = -1,
            });

            SendMessage(new ActTransitionMessage()
            {
                Id = 0x00A8,
                Field0 = 0x00000000,
                Field1 = true,
            });

            #region NewPlayer
            SendMessage(new NewPlayerMessage()
            {
                Id = 0x0031,
                Field0 = 0x00000000, //Party frame (0x00000000 hide, 0x00000001 show)
                Field1 = "", //Owner name?
                ToonName = BnetClient.CurrentToon.Name,
                Field3 = 0x00000002, //party frame class 
                Field4 = 0x00000004, //party frame level
                snoActorPortrait = BnetClient.CurrentToon.ClassSNO, //party frame portrait
                Field6 = 0x00000001,
                #region HeroStateData
                Field7 = new HeroStateData()
                {
                    Field0 = 0x00000000,
                    Field1 = 0x00000000,
                    Field2 = 0x00000000,
                    Field3 = BnetClient.CurrentToon.Gender,
                    Field4 = new PlayerSavedData()
                    {
                        #region HotBarButtonData
                        Field0 = new HotbarButtonData[9]
            {
                 new HotbarButtonData()
                 {
                    m_snoPower = 0x000176C4,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = 0x00007780,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = 0x00007780,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = 0x000216FA,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = 0x622256D4,
                 },
            },
                        #endregion
                        #region SkillKeyMapping
                        Field1 = new SkillKeyMapping[15]
            {
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
            },
                        #endregion
                        Field2 = 0x00000000,
                        Field3 = 0x00000001,
                        #region HirelingSavedData
                        Field4 = new HirelingSavedData()
                        {
                            Field0 = new HirelingInfo[4]
                {
                     new HirelingInfo()
                     {
                        Field0 = 0x00000000,
                        Field1 = -1,
                        Field2 = 0x00000000,
                        Field3 = 0x00000000,
                        Field4 = false,
                        Field5 = -1,
                        Field6 = -1,
                        Field7 = -1,
                        Field8 = -1,
                     },
                     new HirelingInfo()
                     {
                        Field0 = 0x00000000,
                        Field1 = -1,
                        Field2 = 0x00000000,
                        Field3 = 0x00000000,
                        Field4 = false,
                        Field5 = -1,
                        Field6 = -1,
                        Field7 = -1,
                        Field8 = -1,
                     },
                     new HirelingInfo()
                     {
                        Field0 = 0x00000000,
                        Field1 = -1,
                        Field2 = 0x00000000,
                        Field3 = 0x00000000,
                        Field4 = false,
                        Field5 = -1,
                        Field6 = -1,
                        Field7 = -1,
                        Field8 = -1,
                     },
                     new HirelingInfo()
                     {
                        Field0 = 0x00000000,
                        Field1 = -1,
                        Field2 = 0x00000000,
                        Field3 = 0x00000000,
                        Field4 = false,
                        Field5 = -1,
                        Field6 = -1,
                        Field7 = -1,
                        Field8 = -1,
                     },
                },
                            Field1 = 0x00000000,
                            Field2 = 0x00000000,
                        },
                        #endregion
                        Field5 = 0x00000000,
                        #region LearnedLore
                        Field6 = new LearnedLore()
                        {
                            Field0 = 0x00000000,
                            m_snoLoreLearned = new int[256]
                {
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                },
                        },
                        #endregion
                        #region snoActiveSkills
                        snoActiveSkills = new int[6]
            {
                0x000176C4, 0x000216FA, -1, -1, -1, -1, 
            },
                        #endregion
                        #region snoTraits
                        snoTraits = new int[3]
            {
                -1, -1, -1, 
            },
                        #endregion
                        #region SavePointData
                        Field9 = new SavePointData()
                        {
                            snoWorld = -1,
                            Field1 = -1,
                        },
                        #endregion
                        #region SeenTutorials
                        m_SeenTutorials = new int[64]
            {
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
            },
                        #endregion
                    },
                    Field5 = 0x00000000,
                    #region PlayerQuestRewardHistoryEntry
                    tQuestRewardHistory = new PlayerQuestRewardHistoryEntry[0]
        {
        },
                    #endregion
                },
                #endregion
                Field8 = false, //announce party join
                Field9 = 0x00000001,
                Field10 = 0x789E00E2,
            });
            #endregion
            #region GenericBlobMessages 0x0032,0x00ED,0x00EE,0x00EF
            /*SendMessage(new GenericBlobMessage()
            {
                Id = 0x0032,
                Data = new byte[22]
    {
        0x08, 0x00, 0x12, 0x12, 0x08, 0x08, 0x10, 0x03, 0x18, 0x04, 0x20, 0x0B, 0x28, 0x14, 0x30, 0x07, 
        0x38, 0x0B, 0x40, 0x04, 0x48, 0x01, 
    },
            });

            SendMessage(new GenericBlobMessage()
            {
                Id = 0x00ED,
                Data = new byte[11]
    {
        0x18, 0x00, 0x21, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    },
            });

            SendMessage(new GenericBlobMessage()
            {
                Id = 0x00EE,
                Data = new byte[11]
    {
        0x18, 0x00, 0x21, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    },
            });

            SendMessage(new GenericBlobMessage()
            {
                Id = 0x00EF,
                Data = new byte[11]
    {
        0x18, 0x00, 0x21, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    },
            });*/
            #endregion
            #region GameSyncedData
            /*SendMessage(new GameSyncedDataMessage()
                        {
                            Id = 0x00AF,
                            Field0 = new GameSyncedData()
                            {
                                Field0 = false,
                                Field1 = 0x00000000,
                                Field2 = 0x00000000,
                                Field3 = 0x00000000,
                                Field4 = 0x00000000,
                                Field5 = 0x00000000,
                                Field6 = new int[2]
                    {
                        0x00000000, 0x00000000, 
                    },
                                Field7 = new int[2]
                    {
                        0x00000000, 0x00000000, 
                    },
                            },
                        });*/
            #endregion
            #region Interstitial,RevealWorld,WorldStatus,EnterWorld

            SendMessage(new RevealWorldMessage()
            {
                Id = 0x0037,
                Field0 = 0x772E0000,
                Field1 = 0x000115EE,
            });

            SendMessage(new EnterWorldMessage()
            {
                Id = 0x0033,
                Field0 = new Vector3D() { Field0 = 3143.75f, Field1 = 2828.75f, Field2 = 59.07559f },
                Field1 = 0x772E0000,
                Field2 = 0x000115EE,
            });
            #endregion

            ReadAndSendMap();

            #region RevealScene/MapRevealScene
            /*SendMessage(new RevealSceneMessage()
            {
                Id = 0x0034,
                Field0 = 0x772E0000,
                Field1 = new SceneSpecification()
                {
                    Field0 = 0x00000000,
                    Field1 = new IVector2D()
                    {
                        Field0 = 0x00000000,
                        Field1 = 0x00000000,
                    },
                    arSnoLevelAreas = new int[4]
        {
            0x00004DEB, 0x00026186, -1, -1, 
        },
                    snoPrevWorld = -1,
                    Field4 = 0x00000000,
                    snoPrevLevelArea = -1,
                    snoNextWorld = -1,
                    Field7 = 0x00000000,
                    snoNextLevelArea = -1,
                    snoMusic = 0x000206F8,
                    snoCombatMusic = -1,
                    snoAmbient = 0x0002734F,
                    snoReverb = 0x000153F6,
                    snoWeather = 0x00013220,
                    snoPresetWorld = 0x000115EE,
                    Field15 = 0x00000002,
                    Field16 = 0x00000002,
                    Field17 = 0x00000000,
                    Field18 = -1,
                    tCachedValues = new SceneCachedValues()
                    {
                        Field0 = 0x0000003F,
                        Field1 = 0x00000060,
                        Field2 = 0x00000060,
                        Field3 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 115.9387f,
                                Field1 = 125.2578f,
                                Field2 = 43.82879f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 124.0615f,
                                Field1 = 131.6655f,
                                Field2 = 57.26923f,
                            },
                        },
                        Field4 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 115.9387f,
                                Field1 = 125.2578f,
                                Field2 = 43.82879f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 124.0615f,
                                Field1 = 131.6655f,
                                Field2 = 57.26923f,
                            },
                        },
                        Field5 = new int[4]
            {
                0x00000000, 0x000004C8, 0x00000000, 0x00000000, 
            },
                        Field6 = 0x00000009,
                    },
                },
                Field2 = 0x77560002,
                snoScene = 0x00008245,
                Field4 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 3060f,
                        Field1 = 2760f,
                        Field2 = 0f,
                    },
                },
                Field5 = -1,
                snoSceneGroup = -1,
                arAppliedLabels = new int[0]
    {
    },
            });

            SendMessage(new MapRevealSceneMessage()
            {
                Id = 0x0044,
                Field0 = 0x77560002,
                snoScene = 0x00008245,
                Field2 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 3060f,
                        Field1 = 2760f,
                        Field2 = 0f,
                    },
                },
                Field3 = 0x772E0000,
                Field4 = 0x00000002,
            });

/*            SendMessage(new RevealSceneMessage()
            {
                Id = 0x0034,
                Field0 = 0x772E0000,
                Field1 = new SceneSpecification()
                {
                    Field0 = 0x00000000,
                    Field1 = new IVector2D()
                    {
                        Field0 = 0x0000002B,
                        Field1 = 0x0000002E,
                    },
                    arSnoLevelAreas = new int[4]
        {
            0x000316CE, -1, 0x00004DEB, -1, 
        },
                    snoPrevWorld = -1,
                    Field4 = 0x00000000,
                    snoPrevLevelArea = -1,
                    snoNextWorld = -1,
                    Field7 = 0x00000000,
                    snoNextLevelArea = -1,
                    snoMusic = 0x00021AF7,
                    snoCombatMusic = -1,
                    snoAmbient = 0x0002734F,
                    snoReverb = 0x000153F6,
                    snoWeather = 0x00013220,
                    snoPresetWorld = 0x000115EE,
                    Field15 = 0x00000000,
                    Field16 = 0x00000000,
                    Field17 = 0x00000000,
                    Field18 = -1,
                    tCachedValues = new SceneCachedValues()
                    {
                        Field0 = 0x0000003F,
                        Field1 = 0x00000060,
                        Field2 = 0x00000060,
                        Field3 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 119.8279f,
                                Field1 = 126.4012f,
                                Field2 = 36.26942f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 130.6345f,
                                Field1 = 128.5111f,
                                Field2 = 40.50302f,
                            },
                        },
                        Field4 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 122.8899f,
                                Field1 = 126.4012f,
                                Field2 = 36.26942f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 133.6965f,
                                Field1 = 128.5111f,
                                Field2 = 40.50302f,
                            },
                        },
                        Field5 = new int[4]
            {
                0x00000315, 0x00000000, 0x0000009B, 0x00000000, 
            },
                        Field6 = 0x00000009,
                    },
                },
                Field2 = 0x77540000,
                snoScene = 0x00008243,
                Field4 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 2580f,
                        Field1 = 2760f,
                        Field2 = 0f,
                    },
                },
                Field5 = -1,
                snoSceneGroup = -1,
                arAppliedLabels = new int[0]
    {
    },
            });

            SendMessage(new MapRevealSceneMessage()
            {
                Id = 0x0044,
                Field0 = 0x77540000,
                snoScene = 0x00008243,
                Field2 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 2580f,
                        Field1 = 2760f,
                        Field2 = 0f,
                    },
                },
                Field3 = 0x772E0000,
                Field4 = 0x00000002,
            });

            SendMessage(new RevealSceneMessage()
            {
                Id = 0x0034,
                Field0 = 0x772E0000,
                Field1 = new SceneSpecification()
                {
                    Field0 = 0x00000000,
                    Field1 = new IVector2D()
                    {
                        Field0 = 0x0000002F,
                        Field1 = 0x0000002A,
                    },
                    arSnoLevelAreas = new int[4]
        {
            0x00004DEB, -1, -1, -1, 
        },
                    snoPrevWorld = -1,
                    Field4 = 0x00000000,
                    snoPrevLevelArea = -1,
                    snoNextWorld = -1,
                    Field7 = 0x00000000,
                    snoNextLevelArea = -1,
                    snoMusic = 0x000206F8,
                    snoCombatMusic = -1,
                    snoAmbient = 0x0002734F,
                    snoReverb = 0x000153F6,
                    snoWeather = 0x00013220,
                    snoPresetWorld = 0x000115EE,
                    Field15 = 0x00000036,
                    Field16 = 0x00000036,
                    Field17 = 0x00000000,
                    Field18 = 0x00000017,
                    tCachedValues = new SceneCachedValues()
                    {
                        Field0 = 0x0000003F,
                        Field1 = 0x00000060,
                        Field2 = 0x00000060,
                        Field3 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 120.0193f,
                                Field1 = 126.4575f,
                                Field2 = 27.89188f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 128.8389f,
                                Field1 = 133.2742f,
                                Field2 = 41.33233f,
                            },
                        },
                        Field4 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 120.0193f,
                                Field1 = 126.4575f,
                                Field2 = 26.40827f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 128.8389f,
                                Field1 = 133.2742f,
                                Field2 = 42.81593f,
                            },
                        },
                        Field5 = new int[4]
            {
                0x0000093A, 0x00000000, 0x00000000, 0x00000000, 
            },
                        Field6 = 0x00000009,
                    },
                },
                Field2 = 0x778A0036,
                snoScene = 0x0000823E,
                Field4 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 2820f,
                        Field1 = 2520f,
                        Field2 = 0f,
                    },
                },
                Field5 = -1,
                snoSceneGroup = -1,
                arAppliedLabels = new int[0]
    {
    },
            });

            SendMessage(new MapRevealSceneMessage()
            {
                Id = 0x0044,
                Field0 = 0x778A0036,
                snoScene = 0x0000823E,
                Field2 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 2820f,
                        Field1 = 2520f,
                        Field2 = 0f,
                    },
                },
                Field3 = 0x772E0000,
                Field4 = 0x00000002,
            });

            SendMessage(new RevealSceneMessage()
            {
                Id = 0x0034,
                Field0 = 0x772E0000,
                Field1 = new SceneSpecification()
                {
                    Field0 = 0x00000000,
                    Field1 = new IVector2D()
                    {
                        Field0 = 0x00000033,
                        Field1 = 0x0000002A,
                    },
                    arSnoLevelAreas = new int[4]
        {
            0x00004DEB, -1, -1, -1, 
        },
                    snoPrevWorld = -1,
                    Field4 = 0x00000000,
                    snoPrevLevelArea = -1,
                    snoNextWorld = -1,
                    Field7 = 0x00000000,
                    snoNextLevelArea = -1,
                    snoMusic = 0x000206F8,
                    snoCombatMusic = -1,
                    snoAmbient = 0x0002734F,
                    snoReverb = 0x000153F6,
                    snoWeather = 0x00013220,
                    snoPresetWorld = 0x000115EE,
                    Field15 = 0x0000003C,
                    Field16 = 0x0000003C,
                    Field17 = 0x00000000,
                    Field18 = -1,
                    tCachedValues = new SceneCachedValues()
                    {
                        Field0 = 0x0000003F,
                        Field1 = 0x00000060,
                        Field2 = 0x00000060,
                        Field3 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 112.136f,
                                Field1 = 122.7885f,
                                Field2 = 19.60409f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 127.8643f,
                                Field1 = 122.7885f,
                                Field2 = 35.97958f,
                            },
                        },
                        Field4 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 112.136f,
                                Field1 = 122.7885f,
                                Field2 = 19.60409f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 127.8643f,
                                Field1 = 122.7885f,
                                Field2 = 35.97958f,
                            },
                        },
                        Field5 = new int[4]
            {
                0x000000CF, 0x00000000, 0x00000000, 0x00000000, 
            },
                        Field6 = 0x00000009,
                    },
                },
                Field2 = 0x7790003C,
                snoScene = 0x0000823F,
                Field4 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 3060f,
                        Field1 = 2520f,
                        Field2 = 0f,
                    },
                },
                Field5 = -1,
                snoSceneGroup = -1,
                arAppliedLabels = new int[0]
    {
    },
            });

            SendMessage(new MapRevealSceneMessage()
            {
                Id = 0x0044,
                Field0 = 0x7790003C,
                snoScene = 0x0000823F,
                Field2 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 3060f,
                        Field1 = 2520f,
                        Field2 = 0f,
                    },
                },
                Field3 = 0x772E0000,
                Field4 = 0x00000002,
            });

            SendMessage(new RevealSceneMessage()
            {
                Id = 0x0034,
                Field0 = 0x772E0000,
                Field1 = new SceneSpecification()
                {
                    Field0 = 0x00000000,
                    Field1 = new IVector2D()
                    {
                        Field0 = 0x00000033,
                        Field1 = 0x00000026,
                    },
                    arSnoLevelAreas = new int[4]
        {
            -1, -1, -1, -1, 
        },
                    snoPrevWorld = -1,
                    Field4 = -1,
                    snoPrevLevelArea = -1,
                    snoNextWorld = -1,
                    Field7 = -1,
                    snoNextLevelArea = -1,
                    snoMusic = 0x000206F8,
                    snoCombatMusic = -1,
                    snoAmbient = 0x0002734F,
                    snoReverb = 0x000153F6,
                    snoWeather = 0x0000C575,
                    snoPresetWorld = 0x000115EE,
                    Field15 = 0x00000089,
                    Field16 = 0x00000089,
                    Field17 = 0x00000000,
                    Field18 = -1,
                    tCachedValues = new SceneCachedValues()
                    {
                        Field0 = 0x0000003F,
                        Field1 = 0x00000060,
                        Field2 = 0x00000060,
                        Field3 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 119.9998f,
                                Field1 = 120.0008f,
                                Field2 = -6.470332f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 120.0005f,
                                Field1 = 120.0008f,
                                Field2 = 6.970119f,
                            },
                        },
                        Field4 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 119.9998f,
                                Field1 = 120.0008f,
                                Field2 = -6.470333f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 120.0005f,
                                Field1 = 120.0008f,
                                Field2 = 6.97012f,
                            },
                        },
                        Field5 = new int[4]
            {
                0x00000000, 0x00000000, 0x00000000, 0x00000000, 
            },
                        Field6 = 0x00000001,
                    },
                },
                Field2 = 0x77DD0089,
                snoScene = 0x0000823A,
                Field4 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 3060f,
                        Field1 = 2280f,
                        Field2 = 0f,
                    },
                },
                Field5 = -1,
                snoSceneGroup = -1,
                arAppliedLabels = new int[0]
    {
    },
            });

            SendMessage(new MapRevealSceneMessage()
            {
                Id = 0x0044,
                Field0 = 0x77DD0089,
                snoScene = 0x0000823A,
                Field2 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 3060f,
                        Field1 = 2280f,
                        Field2 = 0f,
                    },
                },
                Field3 = 0x772E0000,
                Field4 = 0x00000000,
            });

            SendMessage(new RevealSceneMessage()
            {
                Id = 0x0034,
                Field0 = 0x772E0000,
                Field1 = new SceneSpecification()
                {
                    Field0 = 0x00000000,
                    Field1 = new IVector2D()
                    {
                        Field0 = 0x0000002F,
                        Field1 = 0x0000002E,
                    },
                    arSnoLevelAreas = new int[4]
        {
            0x00004DEB, 0x00026186, 0x000316CE, -1, 
        },
                    snoPrevWorld = -1,
                    Field4 = 0x00000000,
                    snoPrevLevelArea = -1,
                    snoNextWorld = -1,
                    Field7 = 0x00000000,
                    snoNextLevelArea = -1,
                    snoMusic = 0x000206F8,
                    snoCombatMusic = -1,
                    snoAmbient = 0x0002734F,
                    snoReverb = 0x000153F6,
                    snoWeather = 0x00013220,
                    snoPresetWorld = 0x000115EE,
                    Field15 = 0x00000001,
                    Field16 = 0x00000001,
                    Field17 = 0x00000000,
                    Field18 = -1,
                    tCachedValues = new SceneCachedValues()
                    {
                        Field0 = 0x0000003F,
                        Field1 = 0x00000060,
                        Field2 = 0x00000060,
                        Field3 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 118.3843f,
                                Field1 = 119.1316f,
                                Field2 = 43.57133f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 134.2307f,
                                Field1 = 131.5811f,
                                Field2 = 43.57133f,
                            },
                        },
                        Field4 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 113.465f,
                                Field1 = 119.1316f,
                                Field2 = 43.57133f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 139.15f,
                                Field1 = 131.5811f,
                                Field2 = 43.57133f,
                            },
                        },
                        Field5 = new int[4]
            {
                0x0000083F, 0x00000371, 0x000001ED, 0x00000000, 
            },
                        Field6 = 0x00000009,
                    },
                },
                Field2 = 0x77550001,
                snoScene = 0x00008244,
                Field4 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 2820f,
                        Field1 = 2760f,
                        Field2 = 0f,
                    },
                },
                Field5 = -1,
                snoSceneGroup = -1,
                arAppliedLabels = new int[1]
    {
        0x00360E26, 
    },
            });

            SendMessage(new MapRevealSceneMessage()
            {
                Id = 0x0044,
                Field0 = 0x77550001,
                snoScene = 0x00008244,
                Field2 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 2820f,
                        Field1 = 2760f,
                        Field2 = 0f,
                    },
                },
                Field3 = 0x772E0000,
                Field4 = 0x00000002,
            });            
            */
            #endregion

            Console.WriteLine("Positioning character at " + posx + " " + posy + " " + posz);

            #region ACDEnterKnown 0x789E00E2 PlayerId??
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x789E00E2,
                Field1 = BnetClient.CurrentToon.ClassSNO, //Player model?
                Field2 = 0x00000009,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1.43f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.05940768f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.9982339f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = posx,
                            Field1 = posy,
                            Field2 = posz,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = 0x00000007,
                    Field1 = BnetClient.CurrentToon.ClassID,
                },
                Field7 = -1,
                Field8 = -1,
                Field9 = 0x00000000,
                Field10 = 0x00,
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x789E00E2,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x01F8], // SkillKit 
            Int = 0x00008AFA,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00033C40,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00007545,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00007545,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000226,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 0.5f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000226,
            Attribute = GameAttribute.Attributes[0x003C], // Resistance 
            Int = 0x00000000,
            Float = 0.5f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00D7], // Immobolize 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00D6], // Untargetable 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000076B7,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000076B7,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000006DF,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000CE11,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x01D2], // CantStartDisplayedPowers 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000216FA,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000176C4,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000216FA,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000176C4,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000006DF,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000000DE,
            Attribute = GameAttribute.Attributes[0x003C], // Resistance 
            Int = 0x00000000,
            Float = 0.5f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000000DE,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 0.5f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00C8], // Get_Hit_Recovery 
            Int = 0x00000000,
            Float = 6f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00C7], // Get_Hit_Recovery_Per_Level 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00C6], // Get_Hit_Recovery_Base 
            Int = 0x00000000,
            Float = 5f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00007780,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00C5], // Get_Hit_Max 
            Int = 0x00000000,
            Float = 60f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00007780,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00C4], // Get_Hit_Max_Per_Level 
            Int = 0x00000000,
            Float = 10f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00C3], // Get_Hit_Max_Base 
            Int = 0x00000000,
            Float = 50f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000001,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000002,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000003,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000004,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000005,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000006,
            Attribute = GameAttribute.Attributes[0x003E], // Resistance_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00BE], // Dodge_Rating_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x02BA], // IsTrialActor 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x00A8], // Crit_Percent_Cap 
            Int = 0x3F400000,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = BnetClient.CurrentToon.ResourceID,
            Attribute = GameAttribute.Attributes[0x005E], // Resource_Cur 
            Int = 0x43480000,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = BnetClient.CurrentToon.ResourceID,
            Attribute = GameAttribute.Attributes[0x005F], // Resource_Max 
            Int = 0x00000000,
            Float = 200f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = BnetClient.CurrentToon.ResourceID,
            Attribute = GameAttribute.Attributes[0x0061], // Resource_Max_Total 
            Int = 0x43480000,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x009D], // Damage_Weapon_Min_Total_All 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0099], // Damage_Weapon_Delta_Total_All 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = BnetClient.CurrentToon.ResourceID,
            Attribute = GameAttribute.Attributes[0x0068], // Resource_Regen_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Field0 = BnetClient.CurrentToon.ResourceID,
            Attribute = GameAttribute.Attributes[0x006B], // Resource_Effective_Max 
            Int = 0x00000000,
            Float = 200f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x0092], // Damage_Min_Subtotal 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x0091], // Damage_Min_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x0190], // Damage_Weapon_Min_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x018F], // Attacks_Per_Second_Item_CurrentHand 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0189], // Attacks_Per_Second_Item_Total_MainHand 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0089], // Attacks_Per_Second_Total 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0087], // Attacks_Per_Second 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0187], // Attacks_Per_Second_Item_MainHand 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0086], // Attacks_Per_Second_Item_Total 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00033C40,
            Attribute = GameAttribute.Attributes[0x01BE], // Buff_Icon_End_Tick0 
            Int = 0x000003FB,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0084], // Attacks_Per_Second_Item_Subtotal 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0082], // Attacks_Per_Second_Item 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00033C40,
            Attribute = GameAttribute.Attributes[0x01BA], // Buff_Icon_Start_Tick0 
            Int = 0x00000077,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0081], // Hit_Chance 
            Int = 0x00000000,
            Float = 1f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x007F], // Casting_Speed_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x007D], // Casting_Speed 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x007B], // Movement_Scalar_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0002EC66,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000000,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0079], // Movement_Scalar_Capped_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0078], // Movement_Scalar_Subtotal 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0076], // Strafing_Rate_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0075], // Sprinting_Rate_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0074], // Running_Rate_Total 
            Int = 0x00000000,
            Float = 0.3598633f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x018B], // Damage_Weapon_Min_Total_MainHand 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0073], // Walking_Rate_Total 
            Int = 0x00000000,
            Float = 0.2797852f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x018D], // Damage_Weapon_Delta_Total_MainHand 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000001,
            Attribute = GameAttribute.Attributes[0x008E], // Damage_Delta_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000002,
            Attribute = GameAttribute.Attributes[0x008E], // Damage_Delta_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000003,
            Attribute = GameAttribute.Attributes[0x008E], // Damage_Delta_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000004,
            Attribute = GameAttribute.Attributes[0x008E], // Damage_Delta_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000005,
            Attribute = GameAttribute.Attributes[0x008E], // Damage_Delta_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000006,
            Attribute = GameAttribute.Attributes[0x008E], // Damage_Delta_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x008E], // Damage_Delta_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0070], // Running_Rate 
            Int = 0x00000000,
            Float = 0.3598633f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000001,
            Attribute = GameAttribute.Attributes[0x0190], // Damage_Weapon_Min_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000002,
            Attribute = GameAttribute.Attributes[0x0190], // Damage_Weapon_Min_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000003,
            Attribute = GameAttribute.Attributes[0x0190], // Damage_Weapon_Min_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000004,
            Attribute = GameAttribute.Attributes[0x0190], // Damage_Weapon_Min_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000005,
            Attribute = GameAttribute.Attributes[0x0190], // Damage_Weapon_Min_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000006,
            Attribute = GameAttribute.Attributes[0x0190], // Damage_Weapon_Min_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0190], // Damage_Weapon_Min_Total_CurrentHand 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x006F], // Walking_Rate 
            Int = 0x00000000,
            Float = 0.2797852f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000001,
            Attribute = GameAttribute.Attributes[0x0091], // Damage_Min_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000002,
            Attribute = GameAttribute.Attributes[0x0091], // Damage_Min_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000003,
            Attribute = GameAttribute.Attributes[0x0091], // Damage_Min_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000004,
            Attribute = GameAttribute.Attributes[0x0091], // Damage_Min_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000005,
            Attribute = GameAttribute.Attributes[0x0091], // Damage_Min_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000006,
            Attribute = GameAttribute.Attributes[0x0091], // Damage_Min_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000001,
            Attribute = GameAttribute.Attributes[0x0191], // Damage_Weapon_Delta_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000002,
            Attribute = GameAttribute.Attributes[0x0191], // Damage_Weapon_Delta_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000003,
            Attribute = GameAttribute.Attributes[0x0191], // Damage_Weapon_Delta_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000004,
            Attribute = GameAttribute.Attributes[0x0191], // Damage_Weapon_Delta_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000005,
            Attribute = GameAttribute.Attributes[0x0191], // Damage_Weapon_Delta_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000006,
            Attribute = GameAttribute.Attributes[0x0191], // Damage_Weapon_Delta_Total_CurrentHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0091], // Damage_Min_Total 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0191], // Damage_Weapon_Delta_Total_CurrentHand 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x006E], // Movement_Scalar 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000001,
            Attribute = GameAttribute.Attributes[0x0092], // Damage_Min_Subtotal 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000002,
            Attribute = GameAttribute.Attributes[0x0092], // Damage_Min_Subtotal 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000003,
            Attribute = GameAttribute.Attributes[0x0092], // Damage_Min_Subtotal 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000004,
            Attribute = GameAttribute.Attributes[0x0092], // Damage_Min_Subtotal 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000005,
            Attribute = GameAttribute.Attributes[0x0092], // Damage_Min_Subtotal 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000006,
            Attribute = GameAttribute.Attributes[0x0092], // Damage_Min_Subtotal 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0092], // Damage_Min_Subtotal 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0094], // Damage_Weapon_Delta 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0095], // Damage_Weapon_Delta_SubTotal 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0096], // Damage_Weapon_Max 
            Int = 0x00000000,
            Float = 3f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0097], // Damage_Weapon_Max_Total 
            Int = 0x00000000,
            Float = 3f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0098], // Damage_Weapon_Delta_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000CE11,
            Attribute = GameAttribute.Attributes[0x027B], // Trait 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x009B], // Damage_Weapon_Min 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x009C], // Damage_Weapon_Min_Total 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000CE11,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000CE11,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x005C], // Resource_Type_Primary 
            Int = BnetClient.CurrentToon.ResourceID,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 76f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 40f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0050], // Hitpoints_Total_From_Vitality 
            Int = 0x00000000,
            Float = 36f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004F], // Hitpoints_Factor_Vitality 
            Int = 0x00000000,
            Float = 4f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004E], // Hitpoints_Factor_Level 
            Int = 0x00000000,
            Float = 4f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 76f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x024C], // Disabled 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0046], // Loading 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0045], // Invulnerable 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000002,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x0042], // Skill_Total 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000CE11,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789E00E2,
                atKeyVals = new NetAttributeKeyValue[14]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x012C], // Hidden 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0027], // Level_Cap 
            Int = 0x0000000D,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = BnetClient.CurrentToon.Level,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0022], // Experience_Next 
            Int = 0x000004B0,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0021], // Experience_Granted 
            Int = 0x000003E8,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0020], // Armor_Total 
            Int = 0x00000000,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x000C], // Defense 
            Int = 0x00000000,
            Float = 10f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00033C40,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x000B], // Vitality 
            Int = 0x00000000,
            Float = 9f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x000A], // Precision 
            Int = 0x00000000,
            Float = 11f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0009], // Attack 
            Int = 0x00000000,
            Float = 10f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0008], // Shared_Stash_Slots 
            Int = 0x0000000E,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0007], // Backpack_Slots 
            Int = 0x0000003C,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0103], // General_Cooldown 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x789E00E2,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x789E00E2,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x789E00E2,
                Field1 = 3.022712f,
                Field2 = false,
            });

            SendMessage(new PlayerEnterKnownMessage()
            {
                Id = 0x003D,
                Field0 = 0x00000000,
                Field1 = 0x789E00E2,
            });

            SendMessage(new VisualInventoryMessage()
            {
                Id = 0x004E,
                Field0 = 0x789E00E2,
                Field1 = new VisualEquipment()
                {
                    Field0 = new VisualItem[8]
        {
             new VisualItem() //Head
             {
                Field0 = BnetClient.CurrentToon.Equipment.VisualItemList[0].Gbid,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
                Field3 = -1,
             },
             new VisualItem() //Chest
             {
                Field0 = BnetClient.CurrentToon.Equipment.VisualItemList[1].Gbid,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
                Field3 = -1,
             },
             new VisualItem() //Feet
             {
                Field0 = BnetClient.CurrentToon.Equipment.VisualItemList[2].Gbid,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
                Field3 = -1,
             },
             new VisualItem() //Hands
             {
                Field0 = BnetClient.CurrentToon.Equipment.VisualItemList[3].Gbid,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
                Field3 = -1,
             },
             new VisualItem() //Main hand
             {
                Field0 = BnetClient.CurrentToon.Equipment.VisualItemList[4].Gbid,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
                Field3 = -1,
             },
             new VisualItem() //Offhand
             {
                Field0 = BnetClient.CurrentToon.Equipment.VisualItemList[5].Gbid,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
                Field3 = -1,
             },
             new VisualItem() //Shoulders
             {
                Field0 = BnetClient.CurrentToon.Equipment.VisualItemList[6].Gbid,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
                Field3 = -1,
             },
             new VisualItem() //Legs
             {
                Field0 = BnetClient.CurrentToon.Equipment.VisualItemList[7].Gbid,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
                Field3 = -1,
             },
        },
                },
            });

            SendMessage(new PlayerActorSetInitialMessage()
            {
                Id = 0x0039,
                Field0 = 0x789E00E2,
                Field1 = 0x00000000,
            });
            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = BnetClient.CurrentToon.ClassSNO,
                },
            });
            #endregion
            #region ACDEnterKnown ALL Entries
            #region ACDEnterKnown 0x789700DB
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x789700DB,
                Field1 = 0x00001767,
                Field2 = 0x00000018,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1.11f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.9330626f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.3597142f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3108.125f,
                            Field1 = 2882.163f,
                            Field2 = 64.49878f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001767,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00012A04,
                Field13 = 0x00000000,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789700DB,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789700DB,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x789700DB,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789700DB,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x789700DB,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x789700DB,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x789700DB,
                Field1 = 0.7359439f,
                Field2 = false,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x00001767,
                },
            });
            #endregion
            #region ACDEnterKnown 0x789800DC
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x789800DC,
                Field1 = 0x0000157F,
                Field2 = 0x00000008,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.05940768f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.9982339f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3143.104f,
                            Field1 = 2829.936f,
                            Field2 = 59.07556f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0000157F,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00012A04,
                Field13 = 0x00000001,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789800DC,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789800DC,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x789800DC,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789800DC,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x789800DC,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x789800DC,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x789800DC,
                Field1 = 3.022712f,
                Field2 = false,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x0000157F,
                },
            });

            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x789900DD,
                Field1 = 0x000201E5,
                Field2 = 0x00000008,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.9878305f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.1555345f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3109.516f,
                            Field1 = 2803.876f,
                            Field2 = 59.07428f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x000201E5,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00012A04,
                Field13 = 0x00000002,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789900DD,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789900DD,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x789900DD,
                Field1 = 0x00000411,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789900DD,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x789900DD,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x789900DD,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x789900DD,
                Field1 = 0.3123385f,
                Field2 = false,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x000201E5,
                },
            });
            #endregion
            #region ACDEnterKnown 0x789A00DE
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x789A00DE,
                Field1 = 0x00013871,
                Field2 = 0x00000008,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = -0.7652696f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.6437099f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3113.659f,
                            Field1 = 2803.692f,
                            Field2 = 73.26618f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00013871,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00012A04,
                Field13 = 0x00000003,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789A00DE,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789A00DE,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x789A00DE,
                Field1 = 0x00000001,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789A00DE,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x789A00DE,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x789A00DE,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x789A00DE,
                Field1 = 4.884459f,
                Field2 = false,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x00013871,
                },
            });
            #endregion
            #region ACDEnterKnown 0x789B00DF
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x789B00DF,
                Field1 = 0x00000D86,
                Field2 = 0x00000018,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.9976531f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0.009601643f,
                                Field1 = -0.0006524475f,
                                Field2 = -0.06779216f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3127.913f,
                            Field1 = 2830.662f,
                            Field2 = 59.07558f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00000D86,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00012A04,
                Field13 = 0x00000004,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789B00DF,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789B00DF,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x789B00DF,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789B00DF,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x789B00DF,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x789B00DF,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x789B00DF,
                Field1 = 6.147496f,
                Field2 = false,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x00000D86,
                },
            });
            #endregion
            #region ACDEnterKnown 0x789C00E0
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x789C00E0,
                Field1 = 0x00013870,
                Field2 = 0x00000008,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = -0.4954694f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = -0.8686254f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3120.037f,
                            Field1 = 2864.345f,
                            Field2 = 59.07556f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00013870,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00012A04,
                Field13 = 0x00000006,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789C00E0,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789C00E0,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x789C00E0,
                Field1 = 0x00000001,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789C00E0,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x789C00E0,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x789C00E0,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x789C00E0,
                Field1 = 2.104887f,
                Field2 = false,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x00013870,
                },
            });
            #endregion
            #region ACDEnterKnown 0x789F00E3
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x789F00E3,
                Field1 = 0x00001025,
                Field2 = 0x0000001A,
                Field3 = 0x00000001,
                Field4 = null,
                Field5 = new InventoryLocationMessageData()
                {
                    Field0 = 0x789E00E2,
                    Field1 = 0x00000004,
                    Field2 = new IVector2D()
                    {
                        Field0 = 0x00000000,
                        Field1 = 0x00000000,
                    },
                },
                Field6 = new GBHandle()
                {
                    Field0 = 0x00000002,
                    Field1 = BnetClient.CurrentToon.Equipment.VisualItemList[4].Gbid,
                },
                Field7 = -1,
                Field8 = -1,
                Field9 = 0x00000001,
                Field10 = 0x00,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789F00E3,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x789F00E3,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x789F00E3,
                Field1 = 0x00000080,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789F00E3,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x00007780,
            Attribute = GameAttribute.Attributes[0x0041], // Skill 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x009D], // Damage_Weapon_Min_Total_All 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x009C], // Damage_Weapon_Min_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0099], // Damage_Weapon_Delta_Total_All 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x0097], // Damage_Weapon_Max_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x0096], // Damage_Weapon_Max 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x018C], // Damage_Weapon_Min_Total_OffHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x018B], // Damage_Weapon_Min_Total_MainHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x018A], // Attacks_Per_Second_Item_Total_OffHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0089], // Attacks_Per_Second_Total 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0189], // Attacks_Per_Second_Item_Total_MainHand 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0188], // Attacks_Per_Second_Item_OffHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0187], // Attacks_Per_Second_Item_MainHand 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0086], // Attacks_Per_Second_Item_Total 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0084], // Attacks_Per_Second_Item_Subtotal 
            Int = 0x00000000,
            Float = 1.199219f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789F00E3,
                atKeyVals = new NetAttributeKeyValue[15]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0082], // Attacks_Per_Second_Item 
            Int = 0x00000000,
            Float = 1.199219f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x018B], // Damage_Weapon_Min_Total_MainHand 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x018C], // Damage_Weapon_Min_Total_OffHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x018D], // Damage_Weapon_Delta_Total_MainHand 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x018E], // Damage_Weapon_Delta_Total_OffHand 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0094], // Damage_Weapon_Delta 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0095], // Damage_Weapon_Delta_SubTotal 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0096], // Damage_Weapon_Max 
            Int = 0x00000000,
            Float = 3f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0097], // Damage_Weapon_Max_Total 
            Int = 0x00000000,
            Float = 3f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0098], // Damage_Weapon_Delta_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x009B], // Damage_Weapon_Min 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x009C], // Damage_Weapon_Min_Total 
            Int = 0x00000000,
            Float = 2f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0125], // Seed 
            Int = unchecked((int)0xED34A51F),
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0124], // IdentifyCost 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0117], // Item_Equipped 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x789F00E3,
                atKeyVals = new NetAttributeKeyValue[3]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0115], // Item_Quality_Level 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0113], // Durability_Max 
            Int = 0x00000190,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0112], // Durability_Cur 
            Int = 0x00000190,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x789F00E3,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x789F00E3,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x00001025,
                },
            });
            #endregion
            #region ACDEnterKnown 0x78A000E4
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x78A000E4,
                Field1 = 0x00001158,
                Field2 = 0x0000001A,
                Field3 = 0x00000001,
                Field4 = null,
                Field5 = new InventoryLocationMessageData()
                {
                    Field0 = 0x789E00E2,
                    Field1 = 0x00000000,
                    Field2 = new IVector2D()
                    {
                        Field0 = 0x00000000,
                        Field1 = 0x00000000,
                    },
                },
                Field6 = new GBHandle()
                {
                    Field0 = 0x00000002,
                    Field1 = 0x622256D4,
                },
                Field7 = -1,
                Field8 = -1,
                Field9 = 0x00000001,
                Field10 = 0x00,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78A000E4,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78A000E4,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x78A000E4,
                Field1 = 0x00000080,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x78A000E4,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0052], // Hitpoints_Granted 
            Int = 0x00000000,
            Float = 100f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0125], // Seed 
            Int = unchecked((int)0x884DCD35),
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0121], // ItemStackQuantityLo 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0115], // Item_Quality_Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x78A000E4,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x78A000E4,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x00001158,
                },
            });
            #endregion
            #region ACDEnterKnown 0x78BE0102
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x78BE0102,
                Field1 = 0x0001B186,
                Field2 = 0x00000008,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1.13f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.9009878f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = -0.4338445f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3007.198f,
                            Field1 = 2712.854f,
                            Field2 = 23.76516f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0001B186,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000F05D,
                Field13 = 0x00000012,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78BE0102,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78BE0102,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x78BE0102,
                Field1 = 0x00000102,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x78BE0102,
                atKeyVals = new NetAttributeKeyValue[9]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0159], // Conversation_Icon 
            Int = 0x00000000,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0156], // NPC_Is_Operatable 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 8.523438f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0155], // Is_NPC 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 8.523438f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 8.523438f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x78BE0102,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x78BE0102,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x78BE0102,
                Field1 = 5.385688f,
                Field2 = false,
            });

            SendMessage(new SetIdleAnimationMessage()
            {
                Id = 0x00A5,
                Field0 = 0x78BE0102,
                Field1 = 0x00011150,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x0001B186,
                },
            });
            #endregion
            #region ACDEnterKnown 0x78DD0118
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x78DD0118,
                Field1 = 0x0000157E,
                Field2 = 0x00000008,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = -0.01089788f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.9999406f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3125.888f,
                            Field1 = 2602.642f,
                            Field2 = 1.050535f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0000157E,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00011B71,
                Field13 = 0x00000000,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78DD0118,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78DD0118,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x78DD0118,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x78DD0118,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x78DD0118,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x78DD0118,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x78DD0118,
                Field1 = 3.163388f,
                Field2 = false,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x0000157E,
                },
            });
            #endregion
            #region ACDEnterKnown 0x78DE0119
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x78DE0119,
                Field1 = 0x00000D86,
                Field2 = 0x00000018,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3083.982f,
                            Field1 = 2603.142f,
                            Field2 = 0.4151611f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00000D86,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00011B71,
                Field13 = 0x00000001,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78DE0119,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78DE0119,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x78DE0119,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x78DE0119,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x78DE0119,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x78DE0119,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x78DE0119,
                Field1 = 0f,
                Field2 = false,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x00000D86,
                },
            });
            #endregion
            #region ACDEnterKnown 0x78DF011A
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x78DF011A,
                Field1 = 0x000255BB,
                Field2 = 0x00000008,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1.13f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.1261874f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.9920065f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3131.338f,
                            Field1 = 2597.316f,
                            Field2 = 0.9298096f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x000255BB,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x00011B71,
                Field13 = 0x00000002,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78DF011A,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78DF011A,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x78DF011A,
                Field1 = 0x00000001,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x78DF011A,
                atKeyVals = new NetAttributeKeyValue[13]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0158], // NPC_Has_Interact_Options 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x00000000,
            Attribute = GameAttribute.Attributes[0x0159], // Conversation_Icon 
            Int = 0x00000000,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F972,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F972,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0156], // NPC_Is_Operatable 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 14.00781f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0155], // Is_NPC 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 14.00781f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 14.00781f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000004,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x78DF011A,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x78DF011A,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x78DF011A,
                Field1 = 2.888546f,
                Field2 = false,
            });

            SendMessage(new SetIdleAnimationMessage()
            {
                Id = 0x00A5,
                Field0 = 0x78DF011A,
                Field1 = 0x00011150,
            });

            SendMessage(new SNONameDataMessage()
            {
                Id = 0x00D3,
                Field0 = new SNOName()
                {
                    Field0 = 0x00000001,
                    Field1 = 0x000255BB,
                },
            });

            SendMessage(new HeroStateMessage()
            {
                Id = 0x003A,
                Field0 = new HeroStateData()
                {
                    Field0 = 0x00000000,
                    Field1 = 0x00000000,
                    Field2 = 0x00000000,
                    Field3 = 0x02000000,
                    Field4 = new PlayerSavedData()
                    {
                        Field0 = new HotbarButtonData[9]
            {
                 new HotbarButtonData()
                 {
                    m_snoPower = 0x000176C4,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = 0x00007780,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = 0x00007780,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = 0x000216FA,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = -1,
                 },
                 new HotbarButtonData()
                 {
                    m_snoPower = -1,
                    m_gbidItem = 0x622256D4,
                 },
            },
                        Field1 = new SkillKeyMapping[15]
            {
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
                 new SkillKeyMapping()
                 {
                    Power = -1,
                    Field1 = -1,
                    Field2 = 0x00000000,
                 },
            },
                        Field2 = 0x00000000,
                        Field3 = 0x00000001,
                        Field4 = new HirelingSavedData()
                        {
                            Field0 = new HirelingInfo[4]
                {
                     new HirelingInfo()
                     {
                        Field0 = 0x00000000,
                        Field1 = -1,
                        Field2 = 0x00000000,
                        Field3 = 0x00000000,
                        Field4 = false,
                        Field5 = -1,
                        Field6 = -1,
                        Field7 = -1,
                        Field8 = -1,
                     },
                     new HirelingInfo()
                     {
                        Field0 = 0x00000000,
                        Field1 = -1,
                        Field2 = 0x00000000,
                        Field3 = 0x00000000,
                        Field4 = false,
                        Field5 = -1,
                        Field6 = -1,
                        Field7 = -1,
                        Field8 = -1,
                     },
                     new HirelingInfo()
                     {
                        Field0 = 0x00000000,
                        Field1 = -1,
                        Field2 = 0x00000000,
                        Field3 = 0x00000000,
                        Field4 = false,
                        Field5 = -1,
                        Field6 = -1,
                        Field7 = -1,
                        Field8 = -1,
                     },
                     new HirelingInfo()
                     {
                        Field0 = 0x00000000,
                        Field1 = -1,
                        Field2 = 0x00000000,
                        Field3 = 0x00000000,
                        Field4 = false,
                        Field5 = -1,
                        Field6 = -1,
                        Field7 = -1,
                        Field8 = -1,
                     },
                },
                            Field1 = 0x00000000,
                            Field2 = 0x00000000,
                        },
                        Field5 = 0x00000000,
                        Field6 = new LearnedLore()
                        {
                            Field0 = 0x00000000,
                            m_snoLoreLearned = new int[256]
                {
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 
                },
                        },
                        snoActiveSkills = new int[6]
            {
                0x000176C4, 0x000216FA, -1, -1, -1, -1, 
            },
                        snoTraits = new int[3]
            {
                -1, -1, -1, 
            },
                        Field9 = new SavePointData()
                        {
                            snoWorld = -1,
                            Field1 = -1,
                        },
                        m_SeenTutorials = new int[64]
            {
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
                -1, -1, -1, -1, -1, -1, -1, -1, 
            },
                    },
                    Field5 = 0x00000000,
                    tQuestRewardHistory = new PlayerQuestRewardHistoryEntry[0]
        {
        },
                },
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003C,
                Field0 = 0x78BE0102,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003C,
                Field0 = 0x78DF011A,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000000,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000001,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000002,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000003,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000004,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000005,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000006,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000007,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000008,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000009,
                Field1 = 0x00000000,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x0000000A,
                Field1 = 0x00000002,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x0000000B,
                Field1 = 0x00000002,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x0000000C,
                Field1 = 0x00000002,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x0000000D,
                Field1 = 0x00000002,
                Field2 = -1,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x0000000E,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x0000000F,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000010,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000011,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000012,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000013,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000014,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000015,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealTeamMessage()
            {
                Id = 0x0038,
                Field0 = 0x00000016,
                Field1 = 0x00000000,
                Field2 = 0x00000000,
            });

            SendMessage(new RevealSceneMessage()
            {
                Id = 0x0034,
                Field0 = 0x772E0000,
                Field1 = new SceneSpecification()
                {
                    Field0 = 0x00000000,
                    Field1 = new IVector2D()
                    {
                        Field0 = 0x0000002F,
                        Field1 = 0x0000002E,
                    },
                    arSnoLevelAreas = new int[4]
        {
            0x00004DEB, 0x00026186, 0x000316CE, -1, 
        },
                    snoPrevWorld = -1,
                    Field4 = 0x00000000,
                    snoPrevLevelArea = -1,
                    snoNextWorld = -1,
                    Field7 = 0x00000000,
                    snoNextLevelArea = -1,
                    snoMusic = 0x000206F8,
                    snoCombatMusic = -1,
                    snoAmbient = 0x0002734F,
                    snoReverb = 0x000153F6,
                    snoWeather = 0x00013220,
                    snoPresetWorld = 0x000115EE,
                    Field15 = 0x00000001,
                    Field16 = 0x00000001,
                    Field17 = 0x00000000,
                    Field18 = -1,
                    tCachedValues = new SceneCachedValues()
                    {
                        Field0 = 0x0000003F,
                        Field1 = 0x00000060,
                        Field2 = 0x00000060,
                        Field3 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 118.3843f,
                                Field1 = 119.1316f,
                                Field2 = 43.57133f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 134.2307f,
                                Field1 = 131.5811f,
                                Field2 = 43.57133f,
                            },
                        },
                        Field4 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 113.465f,
                                Field1 = 119.1316f,
                                Field2 = 43.57133f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 139.15f,
                                Field1 = 131.5811f,
                                Field2 = 43.57133f,
                            },
                        },
                        Field5 = new int[4]
            {
                0x0000083F, 0x00000371, 0x000001ED, 0x00000000, 
            },
                        Field6 = 0x00000009,
                    },
                },
                Field2 = 0x77550001,
                snoScene = 0x00008244,
                Field4 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 2820f,
                        Field1 = 2760f,
                        Field2 = 0f,
                    },
                },
                Field5 = -1,
                snoSceneGroup = -1,
                arAppliedLabels = new int[1]
    {
        0x00360E26, 
    },
            });

            SendMessage(new MapRevealSceneMessage()
            {
                Id = 0x0044,
                Field0 = 0x77550001,
                snoScene = 0x00008244,
                Field2 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        },
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 2820f,
                        Field1 = 2760f,
                        Field2 = 0f,
                    },
                },
                Field3 = 0x772E0000,
                Field4 = 0x00000002,
            });
            #endregion
            #region ACDEnterKnown 0x77BC0000
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77BC0000,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2922.286f,
                            Field1 = 2796.864f,
                            Field2 = 23.94531f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000002,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77BC0000,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77BC0000,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77BC0000,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77BC0000,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77BC0000,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77BC0000,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77BC0000,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77C10005
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77C10005,
                Field1 = 0x0000192A,
                Field2 = 0x00000000,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.9228876f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = -0.3850694f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2981.73f,
                            Field1 = 2835.009f,
                            Field2 = 24.66344f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0000192A,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000009,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77C10005,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77C10005,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77C10005,
                Field1 = 0x00000080,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77C10005,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77C10005,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77C10005,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77C10005,
                Field1 = 5.492608f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77C50009
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77C50009,
                Field1 = 0x0001FD60,
                Field2 = 0x00000000,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.9997472f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 1.02945E-05f,
                                Field1 = 3.217819E-05f,
                                Field2 = 0.0224885f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2970.619f,
                            Field1 = 2789.915f,
                            Field2 = 23.94531f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0001FD60,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x0000000D,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77C50009,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77C50009,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77C50009,
                Field1 = 0x00000411,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77C50009,
                atKeyVals = new NetAttributeKeyValue[11]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x02BC], // MinimapActive 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000F50B,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000F50B,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0045], // Invulnerable 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77C50009,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77C50009,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77C50009,
                Field1 = 0.04497663f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77C9000D
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77C9000D,
                Field1 = 0x0000157E,
                Field2 = 0x00000000,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.7700912f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.637934f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2922f,
                            Field1 = 2787f,
                            Field2 = 23.94531f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0000157E,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000013,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77C9000D,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77C9000D,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77C9000D,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77C9000D,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77C9000D,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77C9000D,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77C9000D,
                Field1 = 1.383674f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77CB000F
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77CB000F,
                Field1 = 0x0000155A,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2924f,
                            Field1 = 2802f,
                            Field2 = 23.94532f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0000155A,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000029,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77CB000F,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77CB000F,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77CB000F,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77CB000F,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77CB000F,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77CB000F,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77CB000F,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77DA001E
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77DA001E,
                Field1 = 0x00019527,
                Field2 = 0x00000000,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.9999328f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.01160305f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2981.23f,
                            Field1 = 2785.814f,
                            Field2 = 23.94531f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00019527,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000036,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77DA001E,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77DA001E,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77DA001E,
                Field1 = 0x00000001,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77DA001E,
                atKeyVals = new NetAttributeKeyValue[7]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x01C2], // Could_Have_Ragdolled 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77DA001E,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77DA001E,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77DA001E,
                Field1 = 0.02320435f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77DB001F
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77DB001F,
                Field1 = 0x00018B03,
                Field2 = 0x00000000,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2915.467f,
                            Field1 = 2845.922f,
                            Field2 = 23.82193f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00018B03,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000037,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77DB001F,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77DB001F,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77DB001F,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77DB001F,
                atKeyVals = new NetAttributeKeyValue[1]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x0000000A,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77DB001F,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77DB001F,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77DB001F,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77E20026
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77E20026,
                Field1 = 0x0000157E,
                Field2 = 0x00000000,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.7700912f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.637934f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2993.917f,
                            Field1 = 2791.82f,
                            Field2 = 23.94531f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0000157E,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x0000003E,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77E20026,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77E20026,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77E20026,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77E20026,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77E20026,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77E20026,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77E20026,
                Field1 = 1.383674f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77E6002A
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77E6002A,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2990.738f,
                            Field1 = 2828.897f,
                            Field2 = 23.94533f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000046,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77E6002A,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77E6002A,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77E6002A,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77E6002A,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77E6002A,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77E6002A,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77E6002A,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77E8002C
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77E8002C,
                Field1 = 0x0000155A,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2973.246f,
                            Field1 = 2827.689f,
                            Field2 = 24.87411f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0000155A,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000049,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77E8002C,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77E8002C,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77E8002C,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77E8002C,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77E8002C,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77E8002C,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77E8002C,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77EB002F
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77EB002F,
                Field1 = 0x000261CF,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3002.507f,
                            Field1 = 2843.755f,
                            Field2 = 23.94531f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x000261CF,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x0000004E,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77EB002F,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77EB002F,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77EB002F,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77EB002F,
                atKeyVals = new NetAttributeKeyValue[6]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77EB002F,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77EB002F,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77EB002F,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77F00034
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77F00034,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.9329559f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = -0.3599906f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2939.892f,
                            Field1 = 2798.311f,
                            Field2 = 23.94532f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000057,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F00034,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F00034,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77F00034,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77F00034,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77F00034,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77F00034,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77F00034,
                Field1 = 5.546649f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77F20036
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77F20036,
                Field1 = 0x0002B875,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 0.632705f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.7219135f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.6919833f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2993.722f,
                            Field1 = 2782.086f,
                            Field2 = 24.32831f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0002B875,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000059,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F20036,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F20036,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new PortalSpecifierMessage()
            {
                Id = 0x004B,
                Field0 = 0x77F20036,
                Field1 = new ResolvedPortalDestination()
                {
                    snoWorld = 0x0001AB32,
                    Field1 = 0x000000AC,
                    snoDestLevelArea = 0x0001AB91,
                },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77F20036,
                Field1 = 0x00000001,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77F20036,
                atKeyVals = new NetAttributeKeyValue[7]
    {
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x02BC], // MinimapActive 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0056], // Hitpoints_Max_Total 
            Int = 0x00000000,
            Float = 1f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0054], // Hitpoints_Max 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0051], // Hitpoints_Total_From_Level 
            Int = 0x00000000,
            Float = 3.051758E-05f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x004D], // Hitpoints_Cur 
            Int = 0x00000000,
            Float = 0.0009994507f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0026], // Level 
            Int = 0x00000001,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77F20036,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77F20036,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77F20036,
                Field1 = 1.528479f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77F30037
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77F30037,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2921.905f,
                            Field1 = 2782.769f,
                            Field2 = 24.68567f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x0000005B,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F30037,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F30037,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77F30037,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77F30037,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77F30037,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77F30037,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77F30037,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77F7003B
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77F7003B,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2989.97f,
                            Field1 = 2849.609f,
                            Field2 = 23.94532f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000068,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F7003B,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F7003B,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77F7003B,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77F7003B,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77F7003B,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77F7003B,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77F7003B,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77F8003C
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77F8003C,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 3013.641f,
                            Field1 = 2800.703f,
                            Field2 = 23.94532f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000069,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F8003C,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F8003C,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77F8003C,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77F8003C,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77F8003C,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77F8003C,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77F8003C,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77F9003D
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77F9003D,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2972.528f,
                            Field1 = 2799.993f,
                            Field2 = 23.94532f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x0000006A,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F9003D,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77F9003D,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77F9003D,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77F9003D,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77F9003D,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77F9003D,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77F9003D,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77FC0040
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77FC0040,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2894.062f,
                            Field1 = 2819.54f,
                            Field2 = 23.94533f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x0000006D,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77FC0040,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77FC0040,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77FC0040,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77FC0040,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77FC0040,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77FC0040,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77FC0040,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77FD0041
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77FD0041,
                Field1 = 0x00001243,
                Field2 = 0x00000010,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 1f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2938.309f,
                            Field1 = 2814.277f,
                            Field2 = 23.87319f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00001243,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x0000006E,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77FD0041,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77FD0041,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77FD0041,
                Field1 = 0x00000000,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77FD0041,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F96A,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77FD0041,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77FD0041,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77FD0041,
                Field1 = 0f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x77FE0042
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x77FE0042,
                Field1 = 0x00021463,
                Field2 = 0x00000000,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.07654059f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.9970666f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = 2920.87f,
                            Field1 = 2779.655f,
                            Field2 = 23.94531f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x00021463,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000072,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77FE0042,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x77FE0042,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x77FE0042,
                Field1 = 0x00000411,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x77FE0042,
                atKeyVals = new NetAttributeKeyValue[5]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000F50B,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0000F50B,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0045], // Invulnerable 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x77FE0042,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x77FE0042,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x77FE0042,
                Field1 = 2.988367f,
                Field2 = false,
            });
            #endregion
            #region ACDEnterKnown 0x78010045
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = 0x78010045,
                Field1 = 0x0001B603,
                Field2 = 0x00000000,
                Field3 = 0x00000000,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.8008201f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = -0.598905f,
                            },
                        },
                        Field1 = new Vector3D() //Cart
                        {
                            Field0 = 3030.794f,
                            Field1 = 2770.09f,
                            Field2 = 23.94532f,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = -1,
                    Field1 = -1,
                },
                Field7 = 0x00000001,
                Field8 = 0x0001B603,
                Field9 = 0x00000000,
                Field10 = 0x00,
                Field12 = 0x0000DBD3,
                Field13 = 0x00000075,
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78010045,
                Field1 = 0x00000001,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new AffixMessage()
            {
                Id = 0x0048,
                Field0 = 0x78010045,
                Field1 = 0x00000002,
                aAffixGBIDs = new int[0]
    {
    },
            });

            SendMessage(new ACDCollFlagsMessage()
            {
                Id = 0x00A6,
                Field0 = 0x78010045,
                Field1 = 0x00000C21,
            });

            SendMessage(new AttributesSetValuesMessage()
            {
                Id = 0x004D,
                Field0 = 0x78010045,
                atKeyVals = new NetAttributeKeyValue[4]
    {
         new NetAttributeKeyValue()
         {
            Field0 = 0x000FFFFF,
            Attribute = GameAttribute.Attributes[0x01B9], // Buff_Visual_Effect 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F972,
            Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Field0 = 0x0001F972,
            Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0 
            Int = 0x00000001,
            Float = 0f,
         },
         new NetAttributeKeyValue()
         {
            Attribute = GameAttribute.Attributes[0x0043], // TeamID 
            Int = 0x00000000,
            Float = 0f,
         },
    },
            });

            SendMessage(new ACDGroupMessage()
            {
                Id = 0x00B8,
                Field0 = 0x78010045,
                Field1 = -1,
                Field2 = -1,
            });

            SendMessage(new ANNDataMessage()
            {
                Id = 0x003E,
                Field0 = 0x78010045,
            });

            SendMessage(new ACDTranslateFacingMessage()
            {
                Id = 0x0070,
                Field0 = 0x78010045,
                Field1 = 4.99892f,
                Field2 = false,
            });
            #endregion
            #endregion

            FlushOutgoingBuffer();

            SendMessage(new DWordDataMessage() // TICK
            {
                Id = 0x0089,
                Field0 = 0x00000077,
            });

            FlushOutgoingBuffer();

            SendMessage(new AttributeSetValueMessage()
            {
                Id = 0x004C,
                Field0 = 0x789E00E2,
                Field1 = new NetAttributeKeyValue()
                {
                    Attribute = GameAttribute.Attributes[0x005B], // Hitpoints_Healed_Target
                    Int = 0x00000000,
                    Float = 76f,
                },
            });

            SendMessage(new DWordDataMessage() // TICK
            {
                Id = 0x0089,
                Field0 = 0x0000007D,
            });

            FlushOutgoingBuffer();
        }
        public void OnMessage(SimpleMessage msg)
        {
            switch (msg.Id)
            {
                case 0x0030: // Sent with DwordDataMessage(0x0125, Value:0) and SimpleMessage(0x0125)
                    {
                        #region hardcoded1
                        #region HirelingInfo
                        SendMessage(new HirelingInfoUpdateMessage()
                        {
                            Id = 0x009D,
                            Field0 = 0x00000001,
                            Field1 = false,
                            Field2 = -1,
                            Field3 = 0x00000000,
                        });

                        SendMessage(new HirelingInfoUpdateMessage()
                        {
                            Id = 0x009D,
                            Field0 = 0x00000002,
                            Field1 = false,
                            Field2 = -1,
                            Field3 = 0x00000000,
                        });

                        SendMessage(new HirelingInfoUpdateMessage()
                        {
                            Id = 0x009D,
                            Field0 = 0x00000003,
                            Field1 = false,
                            Field2 = -1,
                            Field3 = 0x00000000,
                        });
                        #endregion
                        #region Attribute Values 0x789E00E2
                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x000FFFFF,
                                Attribute = GameAttribute.Attributes[0x015B], // Banter_Cooldown
                                Int = 0x000007C9,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x00020CBE,
                                Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active
                                Int = 0x00000001,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x00033C40,
                                Attribute = GameAttribute.Attributes[0x01CC], // Buff_Active
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Attribute = GameAttribute.Attributes[0x00D7], // Immobolize
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Attribute = GameAttribute.Attributes[0x00D6], // Untargetable
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Attribute = GameAttribute.Attributes[0x01D2], // CantStartDisplayedPowers
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x00020CBE,
                                Attribute = GameAttribute.Attributes[0x01BA], // Buff_Icon_Start_Tick0
                                Int = 0x000000C1,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Attribute = GameAttribute.Attributes[0x024C], // Disabled
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Attribute = GameAttribute.Attributes[0x012C], // Hidden
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x00033C40,
                                Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x00020CBE,
                                Attribute = GameAttribute.Attributes[0x01BE], // Buff_Icon_End_Tick0
                                Int = 0x000007C9,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Attribute = GameAttribute.Attributes[0x0046], // Loading
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x00033C40,
                                Attribute = GameAttribute.Attributes[0x01BE], // Buff_Icon_End_Tick0
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Attribute = GameAttribute.Attributes[0x0045], // Invulnerable
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x00020CBE,
                                Attribute = GameAttribute.Attributes[0x0230], // Buff_Icon_Count0
                                Int = 0x00000001,
                                Float = 0f,
                            },
                        });

                        SendMessage(new AttributeSetValueMessage()
                        {
                            Id = 0x004C,
                            Field0 = 0x789E00E2,
                            Field1 = new NetAttributeKeyValue()
                            {
                                Field0 = 0x00033C40,
                                Attribute = GameAttribute.Attributes[0x01BA], // Buff_Icon_Start_Tick0
                                Int = 0x00000000,
                                Float = 0f,
                            },
                        });
                        #endregion

                        SendMessage(new ACDCollFlagsMessage()
                        {
                            Id = 0x00A6,
                            Field0 = 0x789E00E2,
                            Field1 = 0x00000008,
                        });

                        SendMessage(new DWordDataMessage()
                        {
                            Id = 0x0089,
                            Field0 = 0x000000C1,
                        });
                        #endregion
                        FlushOutgoingBuffer();
                        #region hardcoded2
                        SendMessage(new TrickleMessage()
                        {
                            Id = 0x0042,
                            Field0 = 0x789E00E2,
                            Field1 = BnetClient.CurrentToon.ClassSNO,
                            Field2 = new WorldPlace()
                            {
                                Field0 = new Vector3D()
                                {
                                    Field0 = 3143.75f,
                                    Field1 = 2828.75f,
                                    Field2 = 59.07559f,
                                },
                                Field1 = 0x772E0000,
                            },
                            Field3 = 0x00000000,
                            Field4 = 0x00026186,
                            Field5 = 1f,
                            Field6 = 0x00000001,
                            Field7 = 0x00000024,
                            Field10 = unchecked((int)0x8DFA5D13),
                            Field12 = 0x0000F063,
                        });

                        SendMessage(new DWordDataMessage()
                        {
                            Id = 0x0089,
                            Field0 = 0x000000D1,
                        });
                        #endregion
                        FlushOutgoingBuffer();
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }

        }
        public void OnMessage(GameSetupMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ConnectionEstablishedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(QuitGameMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(DWordDataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(BroadcastTextMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(GenericBlobMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(UInt64DataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(VersionsMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerIndexMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(NewPlayerMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(EnterWorldMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RevealWorldMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RevealSceneMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(DestroySceneMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(SwapSceneMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RevealTeamMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(HeroStateMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDEnterKnownMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ANNDataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerEnterKnownMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDWorldPositionMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDInventoryPositionMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDInventoryUpdateActorSNO msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerActorSetInitialMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(VisualInventoryMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDChangeGBHandleMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(AffixMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(LearnedSkillMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PortalSpecifierMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RareMonsterNamesMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RareItemNameMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(AttributeSetValueMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ProjectileStickMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(AttributesSetValuesMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ChatMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(VictimMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(KillCountMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayAnimationMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateNormalMessage msg)
        {
            if (msg.Field1 != null)
                position = msg.Field1;
        }
        public void OnMessage(ACDTranslateSnappedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateFacingMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateFixedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateArcMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateDetPathMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateDetPathSinMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateDetPathSpiralMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateSyncMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDTranslateFixedUpdateMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDCollFlagsMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(GoldModifiedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayEffectMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayHitEffectMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayHitEffectOverrideMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayNonPositionalSoundMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayErrorSoundMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayMusicMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayCutsceneMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(FlippyMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(NPCInteractOptionsMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PetMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(HirelingInfoUpdateMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(QuestUpdateMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(QuestMeterMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(QuestCounterMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerLevel msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(WaypointActivatedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(AimTargetMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(SetIdleAnimationMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDPickupFailedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(TrickleMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(MapRevealSceneMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(SavePointInfoMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(HearthPortalInfoMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ReturnPointInfoMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(DataIDDataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerInteractMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(TradeMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ActTransitionMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(InterstitialMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RopeEffectMessageACDToACD msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RopeEffectMessageACDToPlace msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(UIElementMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDChangeActorMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerWarpedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(GameSyncedDataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(EndOfTickMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CreateBNetGameMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CreateBNetGameResultMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RequestJoinBNetGameMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(BNetJoinGameRequestResultMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(JoinLANGameMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(NetworkAddressMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(GameIdMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(IntDataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(EntityIdMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CreateHeroMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CreateHeroResultMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(BlizzconCVarsMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(LogoutContextMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(LogoutTickTimeMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(TargetMessage msg)
        {
            if (msg.Field1 == 0x77F20036)
            {
                EnterInn();
                return;
            }

            if (BnetClient.CurrentToon.Class == Core.Toons.ToonClass.Monk)
            {
                if (msg.snoPower == 0x0176c4) // fists of lightning
                {
                    switch (msg.Field5)
                    {
                        case 0:
                            break;
                        case 1:
                            break;
                        case 2:
                            SpawnMob(120932, position.Field0, position.Field1, position.Field2);
                            break;
                        default:
                            Logger.Error("hmm unknown monk kung-fu: {0}", msg.Field5);
                            break;
                    }
                }
            }

            if (objectsSpawned == null || !objectsSpawned.Any(a => a.id == msg.Field1))
            {
                return;
            }

            Mob mob = objectsSpawned.First(a => a.id == msg.Field1);

            SpawnMob(185366, mob.pos.Field0, mob.pos.Field1, mob.pos.Field2);
            FlushOutgoingBuffer();

            System.Threading.Thread.Sleep(400);

            //SpawnMob(86790, mob.pos.Field0, mob.pos.Field1, mob.pos.Field2);

            List<Mob> kills = new List<Mob>();
            foreach (Mob emob in objectsSpawned)
            {
                if (Math.Abs(emob.pos.Field0 - mob.pos.Field0) < 10f &&
                    Math.Abs(emob.pos.Field1 - mob.pos.Field1) < 10f &&
                    Math.Abs(emob.pos.Field2 - mob.pos.Field2) < 10f)
                {
                    kills.Add(emob);
                }
            }

            Logger.Error("meteor killing {0} mobs", kills.Count);

            foreach (Mob x in kills)
            {
                msg.Field1 = x.id;
                objectsSpawned.Remove(x);

                var killAni = new int[]{
                    0x2cd7,
                    0x2cd4,
                    0x01b378,
                    0x2cdc,
                    0x02f2,
                    0x2ccf,
                    0x2cd0,
                    0x2cd1,
                    0x2cd2,
                    0x2cd3,
                    0x2cd5,
                    0x01b144,
                    0x2cd6,
                    0x2cd8,
                    0x2cda,
                    0x2cd9
                };
                SendMessage(new PlayEffectMessage()
                {
                    Id = 0x7a,
                    Field0 = msg.Field1,
                    Field1 = 0x0,
                    Field2 = 0x2,
                });
                SendMessage(new PlayEffectMessage()
                {
                    Id = 0x7a,
                    Field0 = msg.Field1,
                    Field1 = 0xc,
                });
                SendMessage(new PlayHitEffectMessage()
                {
                    Id = 0x7b,
                    Field0 = msg.Field1,
                    Field1 = 0x789E00E2,
                    Field2 = 0x2,
                    Field3 = false,
                });

                SendMessage(new FloatingNumberMessage()
                {
                    Id = 0xd0,
                    Field0 = msg.Field1,
                    Field1 = 9001.0f,
                    Field2 = 0,
                });

                SendMessage(new ANNDataMessage()
                {
                    Id = 0x6d,
                    Field0 = msg.Field1,
                });

                int ani = killAni[rand.Next(killAni.Length)];
                Logger.Info("Ani used: " + ani);

                SendMessage(new PlayAnimationMessage()
                {
                    Id = 0x6c,
                    Field0 = msg.Field1,
                    Field1 = 0xb,
                    Field2 = 0,
                    tAnim = new PlayAnimationMessageSpec[1]
                {
                    new PlayAnimationMessageSpec()
                    {
                        Field0 = 0x2,
                        Field1 = ani,
                        Field2 = 0x0,
                        Field3 = 1f
                    }
                }
                });

                packetId += 10 * 2;
                SendMessage(new DWordDataMessage()
                {
                    Id = 0x89,
                    Field0 = packetId,
                });

                SendMessage(new ANNDataMessage()
                {
                    Id = 0xc5,
                    Field0 = msg.Field1,
                });

                SendMessage(new AttributeSetValueMessage
                {
                    Id = 0x4c,
                    Field0 = msg.Field1,
                    Field1 = new NetAttributeKeyValue
                    {
                        Attribute = GameAttribute.Attributes[0x4d],
                        Float = 0
                    }
                });

                SendMessage(new AttributeSetValueMessage
                {
                    Id = 0x4c,
                    Field0 = msg.Field1,
                    Field1 = new NetAttributeKeyValue
                    {
                        Attribute = GameAttribute.Attributes[0x1c2],
                        Int = 1
                    }
                });

                SendMessage(new AttributeSetValueMessage
                {
                    Id = 0x4c,
                    Field0 = msg.Field1,
                    Field1 = new NetAttributeKeyValue
                    {
                        Attribute = GameAttribute.Attributes[0x1c5],
                        Int = 1
                    }
                });
                SendMessage(new PlayEffectMessage()
                {
                    Id = 0x7a,
                    Field0 = msg.Field1,
                    Field1 = 0xc,
                });
                SendMessage(new PlayEffectMessage()
                {
                    Id = 0x7a,
                    Field0 = msg.Field1,
                    Field1 = 0x37,
                });
                SendMessage(new PlayHitEffectMessage()
                {
                    Id = 0x7b,
                    Field0 = msg.Field1,
                    Field1 = 0x789E00E2,
                    Field2 = 0x2,
                    Field3 = false,
                });
                packetId += 10 * 2;
                SendMessage(new DWordDataMessage()
                {
                    Id = 0x89,
                    Field0 = packetId,
                });
            }
        }
        public void OnMessage(SecondaryAnimationPowerMessage msg)
        {
            //var mdzq = new System.Net.Sockets.TcpClient("localhost", 19991);
            //var query = mdzq.GetStream().ReadString(1000);
            //mdzq.Close();
            //var mymobs = query.Split(',').Select(s => int.Parse(s));
            int[] mymobs = { 6653, 6653, 6653, 6653, 6653, 6653, 6653, 6653, 6653, 6653 };

            //for (var i = 0; i < 1; i++)
            float x = position.Field0;
            float y = position.Field1;
            int i = 0;
            foreach (int mobid in mymobs)
            {
                if ((i % 2) == 0)
                {
                    x += (float)(rand.NextDouble() * 20);
                    y += (float)(rand.NextDouble() * 20);
                }
                else
                {
                    x -= (float)(rand.NextDouble() * 20);
                    y -= (float)(rand.NextDouble() * 20);
                }
                //SpawnMob(mobs[rand.Next(0, mobs.Length)]);
                SpawnMob(mobid, x, y, position.Field2);
                ++i;
            }
        }
        public void OnMessage(SNODataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(TryConsoleCommand msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(TryChatMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(TryWaypointMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(InventoryRequestMoveMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(InventorySplitStackMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(InventoryStackTransferMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(InventoryRequestSocketMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(InventoryRequestUseMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(SocketSpellMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(HelperDetachMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(AssignSkillMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(HirelingRequestLearnSkillMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerChangeHotbarButtonMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(WorldStatusMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(WeatherOverrideMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ComplexEffectAddMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(EffectGroupACDToACDMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDShearMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDGroupMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayConvLineMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(StopConvLineMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(EndConversationMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(HirelingSwapMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(DeathFadeTimeMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(DisplayGameTextMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(GBIDDataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ACDLookAtMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(KillCounterUpdateMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(LowHealthCombatMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(SaviorMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(FloatingNumberMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(FloatingAmountMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RemoveRagdollMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(SNONameDataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(LoreMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(WorldDeletedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(TimedEventStartedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(ActTransitionStartedMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerQuestMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(PlayerDeSyncSnapMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(SalvageResultsMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(MapMarkerInfoMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(DebugActorTooltipMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(BossEncounterMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(EncounterInviteStateMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(BoolDataMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CameraFocusMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CameraZoomMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CameraYawMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(BossZoomMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(EnchantItemMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CraftingResultsMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(DebugDrawPrimMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(CrafterLevelUpMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(GameTestingSamplingStartMessage msg)
        {
            throw new NotImplementedException();
        }
        public void OnMessage(RequestBuffCancelMessage msg)
        {
            throw new NotImplementedException();
        }

        private void EnterInn()
        {
            SendMessage(new RevealWorldMessage()
            {
                Id = 0x037,
                Field0 = 0x772F0001,
                Field1 = 0x0001AB32,
            });

            SendMessage(new WorldStatusMessage()
            {
                Id = 0x0B4,
                Field0 = 0x772F0001,
                Field1 = false,
            });

            SendMessage(new EnterWorldMessage()
            {
                Id = 0x0033,
                Field0 = new Vector3D()
                {
                    Field0 = 83.75f,
                    Field1 = 123.75f,
                    Field2 = 0.2000023f,
                },
                Field1 = 0x772F0001,
                Field2 = 0x0001AB32,
            });

            FlushOutgoingBuffer();

            SendMessage(new RevealSceneMessage()
            {
                Id = 0x0034,
                Field0 = 0x772F0001,
                Field1 = new SceneSpecification()
                {
                    Field0 = 0x00000000,
                    Field1 = new IVector2D()
                    {
                        Field0 = 0x00000000,
                        Field1 = 0x00000000,
                    },
                    arSnoLevelAreas = new int[] { 0x0001AB91, unchecked((int)0xFFFFFFFF), unchecked((int)0xFFFFFFFF), unchecked((int)0xFFFFFFFF) },
                    snoPrevWorld = 0x000115EE,
                    Field4 = 0x000000DF,
                    snoPrevLevelArea = unchecked((int)0xFFFFFFFF),
                    snoNextWorld = 0x00015348,
                    Field7 = 0x000000AC,
                    snoNextLevelArea = unchecked((int)0xFFFFFFFF),
                    snoMusic = 0x000206F8,
                    snoCombatMusic = unchecked((int)0xFFFFFFFF),
                    snoAmbient = 0x0002C68A,
                    snoReverb = 0x00021ABA,
                    snoWeather = 0x00017869,
                    snoPresetWorld = 0x0001AB32,
                    Field15 = 0x00000000,
                    Field16 = 0x00000000,
                    Field17 = 0x00000000,
                    Field18 = unchecked((int)0xFFFFFFFF),
                    tCachedValues = new SceneCachedValues()
                    {
                        Field0 = 0x0000003F,
                        Field1 = 0x00000060,
                        Field2 = 0x00000060,
                        Field3 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 120f,
                                Field1 = 120f,
                                Field2 = 26.61507f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 120f,
                                Field1 = 120f,
                                Field2 = 36.06968f,
                            }
                        },
                        Field4 = new AABB()
                        {
                            Field0 = new Vector3D()
                            {
                                Field0 = 120f,
                                Field1 = 120f,
                                Field2 = 26.61507f,
                            },
                            Field1 = new Vector3D()
                            {
                                Field0 = 120f,
                                Field1 = 120f,
                                Field2 = 36.06968f,
                            }
                        },
                        Field5 = new int[] { 0x00000267, 0x00000000, 0x00000000, 0x00000000, },
                        Field6 = 0x00000001
                    }
                },
                Field2 = 0x78740120,
                snoScene = 0x0001AB2F,
                Field4 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        }
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 0f,
                        Field1 = 0f,
                        Field2 = 0f,
                    }
                },
                Field5 = unchecked((int)0xFFFFFFFF),
                snoSceneGroup = unchecked((int)0xFFFFFFFF),
                arAppliedLabels = new int[0]

            });
            FlushOutgoingBuffer();

            SendMessage(new MapRevealSceneMessage()
            {
                Id = 0x044,
                Field0 = 0x78740120,
                snoScene = 0x0001AB2F,
                Field2 = new PRTransform()
                {
                    Field0 = new Quaternion()
                    {
                        Field0 = 1f,
                        Field1 = new Vector3D()
                        {
                            Field0 = 0f,
                            Field1 = 0f,
                            Field2 = 0f,
                        }
                    },
                    Field1 = new Vector3D()
                    {
                        Field0 = 0f,
                        Field1 = 0f,
                        Field2 = 0f,
                    }
                },
                Field3 = 0x772F0001,
                Field4 = 0
            });
            FlushOutgoingBuffer();

            SendMessage(new ACDEnterKnownMessage
            {
                Id = 0x3B,
                Field0 = 0x7A0800A2,
                Field1 = 0x0000157F,
                Field2 = 8,
                Field3 = 0,
                Field4 = new WorldLocationMessageData
                {
                    Field0 = 1f,
                    Field1 = new PRTransform
                    {
                        Field0 = new Quaternion
                        {
                            Field0 = 0.9909708f,
                            Field1 = new Vector3D
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.1340775f
                            }
                        },
                        Field1 = new Vector3D
                        {
                            Field0 = 82.15131f,
                            Field1 = 122.2867f,
                            Field2 = 0.1000366f
                        }
                    },
                    Field2 = 0x772F0001
                },
                Field6 = new GBHandle
                {
                    Field0 = -1,
                    Field1 = unchecked((int)0xFFFFFFFF)
                },
                Field7 = 0x00000001,
                Field8 = 0x0000157F,
                Field9 = 0,
                Field10 = 0x0,
                Field12 = 0x0001AB8C,
                Field13 = 0x00000000
            });

            FlushOutgoingBuffer();
            SendMessage(new AffixMessage()
            {
                Id = 0x48,
                Field0 = 0x7A0800A2,
                Field1 = 1,
                aAffixGBIDs = new int[0]
            });
            FlushOutgoingBuffer();

            SendMessage(new AffixMessage()
            {
                Id = 0x48,
                Field0 = 0x7A0800A2,
                Field1 = 2,
                aAffixGBIDs = new int[0]
            });
            FlushOutgoingBuffer();

            SendMessage(new PlayerWarpedMessage()
            {
                Id = 0x0B1,
                Field0 = 9,
                Field1 = 0xf,
            });
            FlushOutgoingBuffer();

            SendMessage(new ACDWorldPositionMessage
            {
                Id = 0x3f,
                Field0 = 0x789E00E2,
                Field1 = new WorldLocationMessageData
                {
                    Field0 = 1.43f,
                    Field1 = new PRTransform
                    {
                        Field0 = new Quaternion
                        {
                            Field0 = 0.05940768f,
                            Field1 = new Vector3D
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = 0.9982339f,
                            }
                        },
                        Field1 = new Vector3D
                        {
                            Field0 = 82.15131f,
                            Field1 = 122.2867f,
                            Field2 = 0.1000366f
                        }
                    },
                    Field2 = 0x772F0001
                }
            });

            packetId += 10 * 2;
            SendMessage(new DWordDataMessage()
            {
                Id = 0x89,
                Field0 = packetId,
            });
            FlushOutgoingBuffer();
        }
        private void SpawnMob(int mobId, float x, float y, float z)
        {
            int nId = mobId;

            Mob mob = new Mob();
            mob.pos = new Vector3D();
            mob.pos.Field0 = x;
            mob.pos.Field1 = y;
            mob.pos.Field2 = z;

            if (objectsSpawned == null)
            {
                objectsSpawned = new List<Mob>();
                //objectsSpawned.Add(objectId - 100);
                //objectsSpawned.Add(objectId);
            }

            objectId++;
            mob.id = objectId;
            objectsSpawned.Add(mob);

            #region ACDEnterKnown Hittable Zombie
            SendMessage(new ACDEnterKnownMessage()
            {
                Id = 0x003B,
                Field0 = objectId,
                Field1 = nId,
                Field2 = 0x8,
                Field3 = 0x0,
                Field4 = new WorldLocationMessageData()
                {
                    Field0 = 1.35f,
                    Field1 = new PRTransform()
                    {
                        Field0 = new Quaternion()
                        {
                            Field0 = 0.768145f,
                            Field1 = new Vector3D()
                            {
                                Field0 = 0f,
                                Field1 = 0f,
                                Field2 = -0.640276f,
                            },
                        },
                        Field1 = new Vector3D()
                        {
                            Field0 = x + 5,
                            Field1 = y + 5,
                            Field2 = z,
                        },
                    },
                    Field2 = 0x772E0000,
                },
                Field5 = null,
                Field6 = new GBHandle()
                {
                    Field0 = 1,
                    Field1 = 1,
                },
                Field7 = 0x00000001,
                Field8 = nId,
                Field9 = 0x0,
                Field10 = 0x0,
                Field11 = 0x0,
                Field12 = 0x0,
                Field13 = 0x0
            });
            SendMessage(new AffixMessage()
            {
                Id = 0x48,
                Field0 = objectId,
                Field1 = 0x1,
                aAffixGBIDs = new int[0]
            });
            SendMessage(new AffixMessage()
            {
                Id = 0x48,
                Field0 = objectId,
                Field1 = 0x2,
                aAffixGBIDs = new int[0]
            });
            SendMessage(new ACDCollFlagsMessage
            {
                Id = 0xa6,
                Field0 = objectId,
                Field1 = 0x1
            });

            SendMessage(new AttributesSetValuesMessage
            {
                Id = 0x4d,
                Field0 = objectId,
                atKeyVals = new NetAttributeKeyValue[15] {
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[214],
                        Int = 0
                    },
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[464],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 1048575,
                        Attribute = GameAttribute.Attributes[441],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30582,
                        Attribute = GameAttribute.Attributes[560],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30286,
                        Attribute = GameAttribute.Attributes[560],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30285,
                        Attribute = GameAttribute.Attributes[560],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30284,
                        Attribute = GameAttribute.Attributes[560],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30283,
                        Attribute = GameAttribute.Attributes[560],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30290,
                        Attribute = GameAttribute.Attributes[560],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 79486,
                        Attribute = GameAttribute.Attributes[560],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30286,
                        Attribute = GameAttribute.Attributes[460],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30285,
                        Attribute = GameAttribute.Attributes[460],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30284,
                        Attribute = GameAttribute.Attributes[460],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30283,
                        Attribute = GameAttribute.Attributes[460],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30290,
                        Attribute = GameAttribute.Attributes[460],
                        Int = 1
                    }
                }

            });

            SendMessage(new AttributesSetValuesMessage
            {
                Id = 0x4d,
                Field0 = objectId,
                atKeyVals = new NetAttributeKeyValue[9] {
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[86],
                        Float = 4.546875f
                    },
                    new NetAttributeKeyValue {
                        Field0 = 79486,
                        Attribute = GameAttribute.Attributes[460],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[84],
                        Float = 4.546875f
                    },
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[81],
                        Int = 0
                    },
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[77],
                        Float = 4.546875f
                    },
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[69],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Field0 = 30582,
                        Attribute = GameAttribute.Attributes[460],
                        Int = 1
                    },
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[67],
                        Int = 10
                    },
                    new NetAttributeKeyValue {
                        Attribute = GameAttribute.Attributes[38],
                        Int = 1
                    }
                }

            });


            SendMessage(new ACDGroupMessage
            {
                Id = 0xb8,
                Field0 = objectId,
                Field1 = unchecked((int)0xb59b8de4),
                Field2 = unchecked((int)0xffffffff)
            });

            SendMessage(new ANNDataMessage
            {
                Id = 0x3e,
                Field0 = objectId
            });

            SendMessage(new ACDTranslateFacingMessage
            {
                Id = 0x70,
                Field0 = objectId,
                Field1 = (float)(rand.NextDouble() * 2.0 * Math.PI),
                Field2 = false
            });

            SendMessage(new SetIdleAnimationMessage
            {
                Id = 0xa5,
                Field0 = objectId,
                Field1 = 0x11150
            });

            SendMessage(new SNONameDataMessage
            {
                Id = 0xd3,
                Field0 = new SNOName
                {
                    Field0 = 0x1,
                    Field1 = nId
                }
            });
            #endregion

            packetId += 30 * 2;
            SendMessage(new DWordDataMessage()
            {
                Id = 0x89,
                Field0 = packetId,
            });
            tick += 20;
            SendMessage(new EndOfTickMessage()
            {
                Id = 0x008D,
                Field0 = tick - 20,
                Field1 = tick
            });

        }
    }
}