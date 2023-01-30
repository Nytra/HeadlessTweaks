using FrooxEngine;
using System.Linq;
using CloudX.Shared;
using System.Reflection;

using static CloudX.Shared.MessageManager;
using BaseX;
using FrooxEngine.LogiX.Math;
using FrooxEngine.LogiX.WorldModel;
using System.Runtime.InteropServices;
using FrooxEngine.LogiX.WorldNodes;
using FrooxEngine.LogiX;

using System.Collections.Generic;
using System.Globalization;
using System.CodeDom;
using System;
using NeosModLoader;

namespace HeadlessTweaks
{
    partial class MessageCommands
    {
        public partial class Commands
        {
            // Show help
            // Usage: /help [?command]
            // If no command is given show all commands

            [Command("help", "Shows this help message", "Common", usage: "[?command]")]
            public static void Help(UserMessages userMessages, Message msg, string[] args)
            {
                var messages = new BatchMessageHelper(userMessages);
                // Check if command arg is given
                if (args.Length > 0)
                {
                    var commandStr = args[0];
                    // Check if command exists
                    if (!commands.ContainsKey(commandStr.ToLower()))
                    {
                        userMessages.SendTextMessage($"Command '{commandStr}' not found");
                        return;
                    }
                    var method = commands[commandStr.ToLower()];

                    var attr = method.GetCustomAttribute<CommandAttribute>();

                    if (GetUserPermissionLevel(msg.SenderId) < attr.PermissionLevel)
                    {
                        userMessages.SendTextMessage($"Command '{commandStr}' not found");
                        return;
                    };
                    
                    /* Message to return:
                     * 
                     * /name usage
                     * description
                     * category
                     * aliases
                     */

                    // Command 
                    messages.Add($"/{attr.Name} {attr.Usage}");
                    messages.Add(attr.Description);

                    messages.Add("Category: " + attr.Category);
                    
                    if (attr.Aliases.Length > 0)
                    {
                        messages.Add("Aliases:");
                        foreach (var alias in attr.Aliases)
                        {
                            messages.Add($"/{alias}", true);
                        }
                    }
                    messages.Send();
                    return;
                }





                //var messages = new BatchMessageHelper(userMessages);

                // Iterate over all commands and print them
                var commandList = commands.ToList();

                // Ignore aliases defined in the CommandAttribute
                commandList.RemoveAll(x => x.Value.GetCustomAttribute<CommandAttribute>()?.Name.ToLower() != x.Key.ToLower());


                foreach (var command in commandList)
                {
                    var method = command.Value;
                    var attr = method.GetCustomAttribute<CommandAttribute>();
                    if (attr != null)
                    {
                        // skip if permission level is higher than the user
                        if (GetUserPermissionLevel(msg.SenderId) < attr.PermissionLevel) continue;

                        var message = $"{attr.Name} - {attr.Description}";

                        // if there are aliases, print them too
                        if (attr.Aliases.Length > 0)
                        {
                            message += $"\nAliases: {string.Join(", ", attr.Aliases)}";
                        }

                        messages.Add(message, true);
                    }
                }
                
                messages.Send();
            }

            // Toggle Opt out of auto-invites
            // Usage: /optOut

            [Command("optOut", "Toggles opt out of auto-invites", "Common")]
            public static void OptOut(UserMessages userMessages, Message msg, string[] args)
            {
                var optOut = HeadlessTweaks.config.GetValue(HeadlessTweaks.AutoInviteOptOut);
                if (optOut.Contains(msg.SenderId))
                {
                    optOut.Remove(msg.SenderId);
                    _ = userMessages.SendTextMessage("Opted in to auto-invites");
                }
                else
                {
                    optOut.Add(msg.SenderId);
                    _ = userMessages.SendTextMessage("Opted out of auto-invites");
                }
                HeadlessTweaks.config.Set(HeadlessTweaks.AutoInviteOptOut, optOut);
                HeadlessTweaks.config.Save();
            }

            // Mark all as read
            // Usage: /markAllRead

            [Command("markAllRead", "Marks all messages as read", "Common")]
            public static void MarkAllRead(UserMessages userMessages, Message msg, string[] args)
            {
                userMessages.MarkAllRead();
            }

            // Invite me to a specific world by name or to the current world if no name is given
            // Usage: /reqInvite [?world name...]

            [Command("reqInvite", "Requests an invite to a world", "Common", PermissionLevel.None, usage: "[?world name...]", "requestInvite")]
            public static void ReqInvite(UserMessages userMessages, Message msg, string[] args)
            {
                World world = null;
                if (args.Length < 1)
                {
                    world = Engine.Current.WorldManager.FocusedWorld;
                    goto Invite;

                }
                string worldName = string.Join(" ", args);
                worldName.Trim();
                var worlds = Engine.Current.WorldManager.Worlds.Where(w => w != Userspace.UserspaceWorld);

                world = worlds.Where(w => w.RawName == worldName || w.SessionId == worldName).FirstOrDefault();
                if (world == null)
                {
                    if (int.TryParse(worldName, out var result))
                    {
                        var worldList = worlds.ToList();
                        if (result < 0 || result >= worldList.Count)
                        {
                            _ = userMessages.SendTextMessage("World index out of range");
                            return;
                        }
                        world = worldList[result];
                    }
                    else
                    {
                        _ = userMessages.SendTextMessage("No world found with the name " + worldName);
                        return;
                    }
                }

            Invite:
                // check if user can join world
                if (!CanUserJoin(world, msg.SenderId))
                {
                    _ = userMessages.SendTextMessage("You can't join " + world.Name);
                    return;
                }
                world.AllowUserToJoin(msg.SenderId);
                _ = userMessages.SendInviteMessage(world.GetSessionInfo());
            }

            // Get session orb
            // Usage: /getSessionOrb [?world name...]
            // If no world name is given, it will get the session orb of the user's world

            [Command("getSessionOrb", "Get session orb", "Common", usage: "[?world name...]")]
            public static void GetSessionOrb(UserMessages userMessages, Message msg, string[] args)
            {
                // Get world by name or user world
                World world = GetWorldOrUserWorld(userMessages, string.Join(" ", args), msg.SenderId, true);
                if (world == null) return;

                // check if user can join world
                if (!CanUserJoin(world, msg.SenderId))
                {
                    _ = userMessages.SendTextMessage("You can't join " + world.Name);
                    return;
                }



                world.RunSynchronously(async () =>
                {
                    var orb = world.GetOrb(true);
                    var a = await userMessages.SendObjectMessage(orb);
                    if (a) world.AllowUserToJoin(msg.SenderId);
                });
            }

            // List worlds
            // Usage: /worlds

            [Command("worlds", "List all worlds", "Common")]
            public static void Worlds(UserMessages userMessages, Message msg, string[] args)
            {
                var messages = new BatchMessageHelper(userMessages);
                int num = 0;
                foreach (World world1 in Engine.Current.WorldManager.Worlds.Where(w => w != Userspace.UserspaceWorld && CanUserJoin(w, msg.SenderId)))
                {
                    messages.Add($"[{num}] {world1.Name} | {world1.ActiveUserCount} ({world1.UserCount}) | {world1.AccessLevel}", true);
                    ++num;
                }
                messages.Send();
            }


            // Throw an error
            // Usage: /throwErr

            [Command("throwErr", "Throw Error", "Debug", PermissionLevel.Owner)]
            public static void ThrowError(UserMessages userMessages, Message msg, string[] args)
            {
                throw new System.Exception("Error Thrown");
            }

            
            // Throw an error asynchronously
            // Usage: /throwErrAsync

            [Command("throwErrAsync", "Throw Error Asynchronously", "Debug", PermissionLevel.Owner)]
            public static async System.Threading.Tasks.Task ThrowErrorAsync(UserMessages userMessages, Message msg, string[] args)
            {
                throw new System.Exception("Async Error Thrown");
            }

            [Command("owo", "owo command", "Common")]
            public static void Owo(UserMessages userMessages, Message msg, string[] args)
            {
                _ = userMessages.SendTextMessage("owo what's this?");
            }

            [Command("playTestSound", "Play a test sound", "Common")]
            public static void PlayTestSound(UserMessages userMessages, Message msg, string[] args)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                focusedWorld.RunInUpdates(0, () =>
                {
                    var clipSlot = focusedWorld.AddSlot("AudioClipNonPersistent", false);
                    var staticAudioClip = clipSlot.AttachComponent<StaticAudioClip>();
                    staticAudioClip.URL.Value = new System.Uri("neosdb:///5dbdf5f9761bbcb00c75f68b2b5e036556c45bed58aff6fe143e152580e6dc17.wav");
                    focusedWorld.PlayOneShot(BaseX.float3.Zero, staticAudioClip);
                    clipSlot.RunInUpdates(0, () =>
                    {
                        clipSlot.DestroyPreservingAssets();
                    });
                    //clipSlot.DestroyPreservingAssets();
                });
                _ = userMessages.SendTextMessage("A test sound was played.");
            }

            [Command("spawnBox", "Spawn a test box", "Common")]
            public static void SpawnBox(UserMessages userMessages, Message msg, string[] args)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                focusedWorld.RunInUpdates(0, () =>
                {
                    var owoBox = focusedWorld.AddSlot("owobox");
                    owoBox.GlobalPosition = new BaseX.float3(RandomX.Range(5), 3, RandomX.Range(5));
                    var boxMesh = owoBox.AttachComponent<BoxMesh>();
                    var meshRenderer = owoBox.AttachComponent<MeshRenderer>();
                    meshRenderer.Mesh.Value = boxMesh.ReferenceID;
                    var pbsMetallic = owoBox.AttachComponent<PBS_Metallic>();
                    meshRenderer.Materials.Add(pbsMetallic);
                    var boxCollider = owoBox.AttachComponent<BoxCollider>();
                    var grabbable = owoBox.AttachComponent<Grabbable>();
                    //var childSlot = owoBox.AddSlot("Data");
                    //var valueField = childSlot.AttachComponent<ValueField<System.Single>>();
                    //valueField.Value.Value = 13.37f;

                    //for (int i = 0; i < 10; i++)
                    //{
                    //    childSlot.AttachComponent<ValueField<System.Single>>().Value.Value = (System.Single)i;
                    //}
                });
                _ = userMessages.SendTextMessage("A box was created.");
            }

            [Command("explode", "Make an explode owo", "Common")]
            public static void ExplodeTest(UserMessages userMessages, Message msg, string[] args)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                focusedWorld.RunInUpdates(0, () =>
                {
                    var explodeSlot = focusedWorld.AddSlot("explodeSlot", false);
                    explodeSlot.AttachComponent<ViolentAprilFoolsExplosion>();
                    explodeSlot.Destroy();
                });
                _ = userMessages.SendTextMessage("An explode was created owo.");
            }

            [Command("bigger", "Make a user bigger", "Common", usage: "[?userName...]")]
            public static void MakeUserBigger(UserMessages userMessages, Message msg, string[] args)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                FrooxEngine.User targetUser = FindUserFromUserMessagesOrArgs(userMessages, args);

                if (targetUser == null)
                {
                    _ = userMessages.SendTextMessage($"User not found!");
                    return;
                }

                MySetUserScale(targetUser, 1.25f);

                _ = userMessages.SendTextMessage($"User {targetUser.UserName} was made bigger.");
            }

            [Command("smaller", "Make a user smaller", "Common", usage: "[?userName...]")]
            public static void MakeUserSmaller(UserMessages userMessages, Message msg, string[] args)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                FrooxEngine.User targetUser = FindUserFromUserMessagesOrArgs(userMessages, args);

                if (targetUser == null)
                {
                    _ = userMessages.SendTextMessage($"User not found!");
                    return;
                }

                MySetUserScale(targetUser, 0.75f);

                _ = userMessages.SendTextMessage($"User {targetUser.UserName} was made smaller.");
            }

            public static void MySetUserScale(FrooxEngine.User targetUser, float targetScaleMult, float time = 1f)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                float currentScale = targetUser.Root.GlobalScale;

                focusedWorld.RunInUpdates(0, () =>
                {
                    targetUser.Root.SetUserScale(currentScale * targetScaleMult, time);
                });
            }

            public static FrooxEngine.User FindUserFromUserMessagesOrArgs(UserMessages userMessages, string[] args)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                FrooxEngine.User targetUser = null;
                if (args.Length < 1)
                {
                    targetUser = focusedWorld.FindUser((FrooxEngine.User u) => u.UserID == userMessages.UserId);
                }
                else
                {
                    string userName = string.Join(" ", args);
                    targetUser = focusedWorld.FindUser((FrooxEngine.User u) => u.UserName.ToLower() == userName.ToLower());
                }

                return targetUser;
            }

            //[Command("process", "Processes a slot called 'processTargetSlot' in the root", "Common")]
            //public static void ProcessObjTest(UserMessages userMessages, Message msg, string[] args)
            //{
            //    var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
            //    Slot targetSlot = focusedWorld.RootSlot.FindChild((Slot s) => s.Name == "processTargetSlot", 0);

            //    if (targetSlot == null)
            //    {
            //        _ = userMessages.SendTextMessage($"processTargetSlot not found!");
            //        return;
            //    }

            //    focusedWorld.RunInUpdates(0, () =>
            //    {
            //        foreach(Component c in targetSlot.Components)
            //        {
            //            var componentSlotRoot = targetSlot.AddSlot($"{c.Name}");
            //            componentSlotRoot.AddSlot($"{c.GetType().FullName}");
            //            componentSlotRoot.AddSlot($"{c.ReferenceID.ToString()}");
            //        } 
            //        //targetSlot.AttachComponent<Comment>().Text.Value = "Object processed!";
            //    });

            //    _ = userMessages.SendTextMessage($"Object was processed.");
            //}

            [Command("monopack", "Monopacks a LogiX packed slot called 'monopackTarget' in the root", "Common")]
            public static void MonopackTest(UserMessages userMessages, Message msg, string[] args)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                Slot targetSlot = focusedWorld.RootSlot.FindChild((Slot s) => s.Name == "monopackTarget", 0);

                if (targetSlot == null)
                {
                    _ = userMessages.SendTextMessage($"monopackTarget not found!");
                    return;
                }

                List<Component> components = new List<Component>();

                focusedWorld.RunInUpdates(0, () =>
                {
                    foreach (Slot s in targetSlot.Children)
                    {
                        var logixComponent = s.GetComponent<LogixNode>();
                        components.Add(logixComponent);
                    }
                    targetSlot.DuplicateComponents(components, false);
                    foreach (Slot s in targetSlot.Children)
                    {
                        s.RunInUpdates(0, () =>
                        {
                            s.Destroy();
                        });
                    }
                    
                });

                _ = userMessages.SendTextMessage($"Slot was monopacked.");
            }

            [Command("monopackFolders", "Monopacks a number of LogiX packed slots inside of a slot called 'monopackFoldersTarget' in the root", "Common")]
            public static void MonopackFoldersTest(UserMessages userMessages, Message msg, string[] args)
            {
                var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                Slot targetSlot = focusedWorld.RootSlot.FindChild((Slot s) => s.Name == "monopackFoldersTarget", 0);

                if (targetSlot == null)
                {
                    _ = userMessages.SendTextMessage($"monopackFoldersTarget not found!");
                    return;
                }

                int monopackCount = 0;

                foreach (Slot folder in targetSlot.Children)
                {
                    List<Component> components = new List<Component>();

                    focusedWorld.RunInUpdates(0, () =>
                    {
                        foreach (Slot s in folder.Children)
                        {
                            var logixComponent = s.GetComponent<LogixNode>();
                            components.Add(logixComponent);
                        }
                        folder.DuplicateComponents(components, false);
                        foreach (Slot s in folder.Children)
                        {
                            s.RunInUpdates(0, () =>
                            {
                                s.Destroy();
                            });
                        }

                    });

                    monopackCount++;
                }

                _ = userMessages.SendTextMessage($"{monopackCount} folders were monopacked.");
            }

            //[Command("unmonopack", "Un-Monopacks a monopacked LogiX slot called 'unMonopackTargetSlot' in the root (not working)", "Common")]
            //public static void UnMonopackTest(UserMessages userMessages, Message msg, string[] args)
            //{
            //    var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
            //    Slot targetSlot = focusedWorld.RootSlot.FindChild((Slot s) => s.Name == "unMonopackTargetSlot", 0);

            //    if (targetSlot == null)
            //    {
            //        _ = userMessages.SendTextMessage($"unMonopackTargetSlot not found!");
            //        return;
            //    }

            //    List<Component> components = targetSlot.Components.ToList<Component>();
            //    List<Component> temp = new List<Component>();

            //    focusedWorld.RunInUpdates(0, () =>
            //    {
            //        foreach (Component c in components)
            //        {
            //            foreach( ISyncMember sm in c.SyncMembers)
            //            {
            //                NeosMod.Msg($"SyncMember: {sm.ToString()}");
            //                NeosMod.Msg($"ActiveLink: {sm.ActiveLink.ToString()}");
            //            }
            //            int i = 0;
            //            while (true)
            //            {
            //                try
            //                {
            //                    var sm = c.GetSyncMember(i);
            //                    NeosMod.Msg($"SyncMember[{i}]: {sm.Name}, {sm.Parent.ToString()}");
            //                }
            //                catch (Exception e)
            //                {
            //                    NeosMod.Msg(e.Message);
            //                    break;
            //                }
            //            }
            //            Slot s = targetSlot.AddSlot(LogixHelper.GetNodeName((c as LogixNode).GetType()));
            //            temp.Add(c);
            //            s.DuplicateComponents(temp, false);
            //            temp.Clear();
            //        }
            //        //foreach (Component c in components)
            //        //{
            //        //    c.Destroy();
            //        //}
            //    });

            //    _ = userMessages.SendTextMessage($"Slot was Un-Monopacked.");
            //}
        }
    }
}
