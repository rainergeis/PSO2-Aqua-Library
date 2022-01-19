﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace AquaModelLibrary
{
    public class ModelImporter
    {
        public static void AssimpAQMConvert(string initialFilePath, bool playerExport, bool useScaleFrames, float scaleFactor)
        {
            float baseScale = 1f / 100f * scaleFactor; //We assume that this will be 100x the true scale because 1 unit to 1 meter isn't the norm
            Assimp.AssimpContext context = new Assimp.AssimpContext();
            context.SetConfig(new Assimp.Configs.FBXPreservePivotsConfig(false));
            Assimp.Scene aiScene = context.ImportFile(initialFilePath, Assimp.PostProcessSteps.Triangulate | Assimp.PostProcessSteps.JoinIdenticalVertices | Assimp.PostProcessSteps.FlipUVs);

            string inputFilename = Path.GetFileNameWithoutExtension(initialFilePath);
            List<string> aqmNames = new List<string>(); //Leave off extensions in case we want this to be .trm later
            List<AquaMotion> aqmList = new List<AquaMotion>();
            Dictionary<int, Assimp.Node> aiNodes = GetAnimatedNodes(aiScene);
            var nodeKeys = aiNodes.Keys.ToList();
            nodeKeys.Sort();
            int animatedNodeCount = nodeKeys.Last() + 1;
            AquaUtil aqua = new AquaUtil();

            for(int i = 0; i < aiScene.Animations.Count; i++)
            {
                if(aiScene.Animations[i] == null)
                {
                    continue;
                }
                AquaMotion aqm = new AquaMotion();
                var anim = aiScene.Animations[i];
                int animEndFrame = 0; //We'll fill this later. Assumes frame 0 to be the start

                if(anim.Name != null && anim.Name != "")
                {
                    //Make sure we're not overwriting anims that somehow have duplicate names
                    if(aqmNames.Contains(anim.Name))
                    {
                        aqmNames.Add($"Anim_{i}_" + anim.Name + ".aqm");
                    } else
                    {
                        aqmNames.Add(anim.Name + ".aqm");
                    }
                } else
                {
                    aqmNames.Add($"Anim_{i}_"  + inputFilename + ".aqm");
                }

                aqm.moHeader = new AquaMotion.MOHeader();

                //Get anim fps
                if (anim.TicksPerSecond == 0)
                {
                    //Default to 30
                    aqm.moHeader.frameSpeed = 30;
                } else
                {
                    aqm.moHeader.frameSpeed = (float)anim.TicksPerSecond;
                }
                
                aqm.moHeader.unkInt0 = 2; //Always, always 2 for NIFL
                aqm.moHeader.variant = 0x2; //These are flags for the animation to tell the game what type it is. Since this is a skeletal animation, we always put 2 here.
                //If it's a player one specifically, the game generally adds 0x10 to this.
                aqm.moHeader.nodeCount = animatedNodeCount;
                if (playerExport)
                {
                    aqm.moHeader.variant += 0x10;
                    aqm.moHeader.nodeCount++; //Add an extra for the nodeTreeFlag 'node'
                }
                aqm.moHeader.testString.SetString("test");

                //Set this ahead of time in case these are out of order
                aqm.motionKeys = new List<AquaMotion.KeyData>(new AquaMotion.KeyData[aqm.moHeader.nodeCount]);
                
                //Nodes
                foreach(var animNode in anim.NodeAnimationChannels)
                {
                    if (animNode == null)
                    {
                        continue;
                    }

                    int id = GetNodeNumber(animNode.NodeName);

                    var node = aqm.motionKeys[id] = new AquaMotion.KeyData();

                    node.mseg.nodeName.SetString(animNode.NodeName);
                    node.mseg.nodeId = id;
                    node.mseg.nodeType = 2;
                    node.mseg.nodeDataCount = useScaleFrames ? 3 : 2;

                    if (animNode.HasPositionKeys)
                    {
                        AquaMotion.MKEY posKeys = new AquaMotion.MKEY();
                        posKeys.keyType = 1;
                        posKeys.dataType = 1;
                        var first = true;
                        foreach (var pos in animNode.PositionKeys)
                        {
                            posKeys.vector4Keys.Add(new Vector4(pos.Value.X * baseScale, pos.Value.Y * baseScale, pos.Value.Z * baseScale, 0));

                            //Account for first frame difference
                            if (first)
                            {
                                posKeys.frameTimings.Add(1);
                                first = false;
                            }
                            else
                            {
                                posKeys.frameTimings.Add((ushort)(pos.Time * 0x10));
                            }
                            posKeys.keyCount++;
                        }
                        posKeys.frameTimings[posKeys.keyCount - 1] += 2; //Account for final frame bitflags

                        animEndFrame = Math.Max(animEndFrame, posKeys.keyCount);
                        node.keyData.Add(posKeys);
                    }

                    if (animNode.HasRotationKeys)
                    {
                        AquaMotion.MKEY rotKeys = new AquaMotion.MKEY();
                        rotKeys.keyType = 2;
                        rotKeys.dataType = 3;
                        var first = true;
                        foreach (var rot in animNode.RotationKeys)
                        {
                            rotKeys.vector4Keys.Add(new Vector4(rot.Value.X, rot.Value.Y, rot.Value.Z, rot.Value.W));

                            //Account for first frame difference
                            if (first)
                            {
                                rotKeys.frameTimings.Add(1);
                                first = false;
                            }
                            else
                            {
                                rotKeys.frameTimings.Add((ushort)(rot.Time * 0x10));
                            }
                            rotKeys.keyCount++;
                        }
                        rotKeys.frameTimings[rotKeys.keyCount - 1] += 2; //Account for final frame bitflags

                        animEndFrame = Math.Max(animEndFrame, rotKeys.keyCount);
                        node.keyData.Add(rotKeys);
                    }

                    if (animNode.HasScalingKeys)
                    {
                        AquaMotion.MKEY sclKeys = new AquaMotion.MKEY();
                        sclKeys.keyType = 2;
                        sclKeys.dataType = 3;
                        var first = true;
                        foreach (var scl in animNode.ScalingKeys)
                        {
                            sclKeys.vector4Keys.Add(new Vector4(scl.Value.X, scl.Value.Y, scl.Value.Z, 0));

                            //Account for first frame difference
                            if(first)
                            {
                                sclKeys.frameTimings.Add(1);
                                first = false;
                            } else
                            {
                                sclKeys.frameTimings.Add((ushort)(scl.Time * 0x10));
                            }
                            sclKeys.keyCount++;
                        }
                        sclKeys.frameTimings[sclKeys.keyCount - 1] += 2; //Account for final frame bitflags

                        animEndFrame = Math.Max(animEndFrame, sclKeys.keyCount);
                        node.keyData.Add(sclKeys);
                    }
                }

                //NodeTreeFlag
                if(playerExport)
                {
                    var node = aqm.motionKeys[aqm.motionKeys.Count - 1] = new AquaMotion.KeyData();
                    node.mseg.nodeName.SetString("__NodeTreeFlag__");
                    node.mseg.nodeId = aqm.motionKeys.Count - 1;
                    node.mseg.nodeType = 0x10;
                    node.mseg.nodeDataCount = useScaleFrames ? 3 : 2;

                    //Position
                    AquaMotion.MKEY posKeys = new AquaMotion.MKEY();
                    posKeys.keyType = 0x10;
                    posKeys.dataType = 5;
                    posKeys.keyCount = animEndFrame + 1;
                    for(int frame = 0; frame < posKeys.keyCount; frame++)
                    {
                        if(frame == 0)
                        {
                            posKeys.frameTimings.Add(0x9);
                        } else if(frame == posKeys.keyCount - 1)
                        {
                            posKeys.frameTimings.Add((ushort)((frame * 0x10) + 0xA));
                        } else
                        {
                            posKeys.frameTimings.Add((ushort)((frame * 0x10) + 0x8));
                        }
                        posKeys.intKeys.Add(0x31);
                    }
                    node.keyData.Add(posKeys);

                    //Rotation
                    AquaMotion.MKEY rotKeys = new AquaMotion.MKEY();
                    rotKeys.keyType = 0x11;
                    rotKeys.dataType = 5;
                    rotKeys.keyCount = animEndFrame + 1;
                    for (int frame = 0; frame < rotKeys.keyCount; frame++)
                    {
                        if (frame == 0)
                        {
                            rotKeys.frameTimings.Add(0x9);
                        }
                        else if (frame == rotKeys.keyCount - 1)
                        {
                            rotKeys.frameTimings.Add((ushort)((frame * 0x10) + 0xA));
                        }
                        else
                        {
                            rotKeys.frameTimings.Add((ushort)((frame * 0x10) + 0x8));
                        }
                        rotKeys.intKeys.Add(0x31);
                    }
                    node.keyData.Add(rotKeys);

                    //Scale
                    if (useScaleFrames)
                    {
                        AquaMotion.MKEY sclKeys = new AquaMotion.MKEY();
                        sclKeys.keyType = 0x12;
                        sclKeys.dataType = 5;
                        sclKeys.keyCount = animEndFrame + 1;
                        for (int frame = 0; frame < sclKeys.keyCount; frame++)
                        {
                            if (frame == 0)
                            {
                                sclKeys.frameTimings.Add(0x9);
                            }
                            else if (frame == sclKeys.keyCount - 1)
                            {
                                sclKeys.frameTimings.Add((ushort)((frame * 0x10) + 0xA));
                            }
                            else
                            {
                                sclKeys.frameTimings.Add((ushort)((frame * 0x10) + 0x8));
                            }
                            sclKeys.intKeys.Add(0x31);
                        }
                        node.keyData.Add(sclKeys);
                    }
                }

                //Sanity check
                foreach(var aiPair in aiNodes)
                {
                    var node = aqm.motionKeys[aiPair.Key];
                    var aiNode = aiPair.Value;
                    if(node == null)
                    {
                        node = aqm.motionKeys[aiPair.Key] = new AquaMotion.KeyData();

                        node.mseg.nodeName.SetString(aiNode.Name);
                        node.mseg.nodeId = aiPair.Key;
                        node.mseg.nodeType = 2;
                        node.mseg.nodeDataCount = useScaleFrames ? 3 : 2;

                        //Position
                        AddOnePosFrame(node, aiNode, baseScale);

                        //Rotation
                        AddOneRotFrame(node, aiNode);

                        //Scale
                        AddOneScaleFrame(useScaleFrames, node);
                    } else
                    {
                        if(node.keyData[0].vector4Keys.Count < 1)
                        {
                            AddOnePosFrame(node, aiNode, baseScale);
                        }

                        if (node.keyData[1].vector4Keys.Count < 1)
                        {
                            AddOneRotFrame(node, aiNode);
                        }

                        if (useScaleFrames && node.keyData[2].vector4Keys.Count < 1)
                        {
                            AddOneScaleFrame(useScaleFrames, node, aiNode);
                        }
                    }
                }

                aqmList.Add(aqm);
            }

            for(int i = 0; i < aqmList.Count; i++)
            {
                var aqm = aqmList[i];
                AquaUtil.AnimSet set = new AquaUtil.AnimSet();
                set.anims.Add(aqm);
                aqua.aquaMotions.Add(set);
                aqua.WriteNIFLMotion(initialFilePath + "_" + aqmNames[i]);

                aqua.aquaMotions.Clear();
            }
        }

        private static void AddOneScaleFrame(bool useScaleFrames, AquaMotion.KeyData node, Assimp.Node aiNode = null)
        {
            if (useScaleFrames)
            {
                AquaMotion.MKEY sclKeys = new AquaMotion.MKEY();
                sclKeys.keyType = 3;
                sclKeys.dataType = 1;
                sclKeys.keyCount = 1;
                if(aiNode == null)
                {
                    sclKeys.vector4Keys = new List<Vector4>() { new Vector4(1.0f, 1.0f, 1.0f, 0) };
                } else
                {
                    Matrix4x4.Invert(GetMat4FromAssimpMat4(aiNode.Transform), out Matrix4x4 mat4);
                    sclKeys.vector4Keys = new List<Vector4>() { new Vector4(mat4.M11, mat4.M22, mat4.M33, 0) };
                }
                node.keyData.Add(sclKeys);
            }
        }

        private static void AddOneRotFrame(AquaMotion.KeyData node, Assimp.Node aiNode)
        {
            AquaMotion.MKEY rotKeys = new AquaMotion.MKEY();
            rotKeys.keyType = 2;
            rotKeys.dataType = 3;
            rotKeys.keyCount = 1;
            Matrix4x4.Invert(GetMat4FromAssimpMat4(aiNode.Transform), out Matrix4x4 mat4);
            var quat = Quaternion.CreateFromRotationMatrix(mat4);
            rotKeys.vector4Keys = new List<Vector4>() { new Vector4(quat.X, quat.Y, quat.Z, quat.W) };
            node.keyData.Add(rotKeys);
        }

        private static void AddOnePosFrame(AquaMotion.KeyData node, Assimp.Node aiNode, float baseScale)
        {
            AquaMotion.MKEY posKeys = new AquaMotion.MKEY();
            posKeys.keyType = 1;
            posKeys.dataType = 1;
            posKeys.keyCount = 1;
            var mat4 = GetMat4FromAssimpMat4(aiNode.Transform);
            posKeys.vector4Keys = new List<Vector4>() { new Vector4(mat4.M14 * baseScale, mat4.M24 * baseScale, mat4.M34 * baseScale, 0) };
            node.keyData.Add(posKeys);
        }

        public static string FilterAnimatedNodeName(string name)
        {
            return name.Substring(name.IndexOf(')') + 1);
        }

        public static int GetNodeNumber(string name)
        {
            string num = name.Split('(')[1].Split(')')[0];
            
            if(Int32.TryParse(num, out int result))
            {
                return result;
            } else
            {
                return -1;
            }
        }

        public static Dictionary<int, Assimp.Node> GetAnimatedNodes(Assimp.Scene aiScene)
        {
            Dictionary<int, Assimp.Node> nodes = new Dictionary<int, Assimp.Node>();

            foreach(var node in aiScene.RootNode.Children)
            {
                CollectAnimated(node, nodes);
            }

            return nodes;
        }

        public static void CollectAnimated(Assimp.Node node, Dictionary<int, Assimp.Node> nodes)
        {
            //Be extra sure that this isn't an effect node
            if (IsAnimatedNode(node, out string name, out int num))
            {  
                //For now, assume animated node is numbered and we can add it directly
                nodes.Add(num, node);
            }

            foreach (var childNode in node.Children)
            {
                CollectAnimated(childNode, nodes);
            }
        }

        //Check if a node is an animated by node by checking if it has the numbering formatting and is NOT marked as an effect node. Output name 
        public static bool IsAnimatedNode(Assimp.Node node, out string name, out int num)
        {
            bool isNumbered = IsNumberedAnimated(node, out name, out num);
            return isNumbered && !node.Name.Contains("#Eff");
        }

        //Check if node is numbered as an animated node. Output name
        private static bool IsNumberedAnimated(Assimp.Node node, out string name, out int num)
        {
            name = node.Name;
            num = -1;
            if(node.Name[0] == '(' && node.Name.Contains(')'))
            {
                if (!int.TryParse(name.Split('(')[1].Split(')')[0], out num))
                {
                    //Make sure this is actually -1 if not parssed properly
                    num = -1;
                };

                return true;
            }
            return false;
        }

        public static void AssimpPRMConvert(string initialFilePath, string finalFilePath)
        {
            Assimp.AssimpContext context = new Assimp.AssimpContext();
            context.SetConfig(new Assimp.Configs.FBXPreservePivotsConfig(false));
            Assimp.Scene aiScene = context.ImportFile(initialFilePath, Assimp.PostProcessSteps.Triangulate | Assimp.PostProcessSteps.JoinIdenticalVertices | Assimp.PostProcessSteps.FlipUVs);
            
            PRMModel prm = new PRMModel();

            int totalVerts = 0;

            //Iterate through and combine meshes. PRMs can only have a single mesh
            IterateAiNodesPRM(prm, ref totalVerts, aiScene, aiScene.RootNode, Matrix4x4.Transpose(GetMat4FromAssimpMat4(aiScene.RootNode.Transform)));
            AquaUtil.WritePRMToFile(prm, finalFilePath, 4);
        }

        private static void IterateAiNodesPRM(PRMModel prm, ref int totalVerts, Assimp.Scene aiScene, Assimp.Node node, Matrix4x4 parentTfm)
        {
            Matrix4x4 nodeMat = Matrix4x4.Transpose(GetMat4FromAssimpMat4(node.Transform));
            nodeMat = Matrix4x4.Multiply(nodeMat, parentTfm);
            
            foreach (int meshId in node.MeshIndices)
            {
                var mesh = aiScene.Meshes[meshId];
                AddAiMeshToPRM(prm, ref totalVerts, mesh, nodeMat);
            }

            foreach(var childNode in node.Children)
            {
                IterateAiNodesPRM(prm, ref totalVerts, aiScene, childNode, nodeMat);
            }
        }

        private static void AddAiMeshToPRM(PRMModel prm, ref int totalVerts, Assimp.Mesh aiMesh, Matrix4x4 nodeMat)
        {
            //Convert vertices
            for (int vertId = 0; vertId < aiMesh.VertexCount; vertId++)
            {
                PRMModel.PRMVert vert = new PRMModel.PRMVert();
                var aiPos = aiMesh.Vertices[vertId];
                var newPos = (new Vector3(aiPos.X, aiPos.Y, aiPos.Z));
                vert.pos = Vector3.Transform(newPos, nodeMat) / 100;

                if (aiMesh.HasVertexColors(0))
                {
                    var aiColor = aiMesh.VertexColorChannels[0][vertId];
                    vert.color = new byte[] { (byte)(aiColor.B * 255), (byte)(aiColor.G * 255), (byte)(aiColor.R * 255), (byte)(aiColor.A * 255) };
                }
                else
                {
                    vert.color = new byte[4];
                }

                if (aiMesh.HasNormals)
                {
                    var aiNorm = aiMesh.Normals[vertId];
                    var normal = new Vector3(aiNorm.X, aiNorm.Y, aiNorm.Z);
                    vert.normal = Vector3.TransformNormal(normal, nodeMat);
                }
                else
                {
                    vert.normal = new Vector3();
                }

                if (aiMesh.HasTextureCoords(0))
                {
                    var aiUV1 = aiMesh.TextureCoordinateChannels[0][vertId];
                    vert.uv1 = new Vector2(aiUV1.X, aiUV1.Y);
                }
                else
                {
                    vert.uv1 = new Vector2();
                }

                if (aiMesh.HasTextureCoords(1))
                {
                    var aiUV2 = aiMesh.TextureCoordinateChannels[1][vertId];
                    vert.uv2 = new Vector2(aiUV2.X, aiUV2.Y);
                }
                else
                {
                    vert.uv2 = new Vector2();
                }

                prm.vertices.Add(vert);
            }

            //Convert Faces
            foreach (var aiFace in aiMesh.Faces)
            {
                prm.faces.Add(new Vector3(aiFace.Indices[0] + totalVerts, aiFace.Indices[1] + totalVerts, aiFace.Indices[2] + totalVerts));
            }

            //Keep count up to date for next potential loop
            totalVerts = prm.vertices.Count;
        }

        //Takes in an Assimp model and generates a full PSO2 model and skeleton from it.
        public static void AssimpAquaConvertFull(string initialFilePath, string finalFilePath, float scaleFactor, bool preAssignNodeIds, bool isNGS)
        {
            float baseScale = 1f / 100f * scaleFactor; //We assume that this will be 100x the true scale because 1 unit to 1 meter isn't the norm
            Assimp.AssimpContext context = new Assimp.AssimpContext();
            context.SetConfig(new Assimp.Configs.FBXPreservePivotsConfig(false));
            Assimp.Scene aiScene = context.ImportFile(initialFilePath, Assimp.PostProcessSteps.Triangulate | Assimp.PostProcessSteps.JoinIdenticalVertices | Assimp.PostProcessSteps.FlipUVs);

            AquaObject aqp = new NGSAquaObject();
            AquaNode aqn = new AquaNode();
            if (isNGS)
            {
                aqp = new NGSAquaObject();
            } else {
                aqp = new ClassicAquaObject();
            }

            //Construct Materials
            Dictionary<string, int> matNameTracker = new Dictionary<string, int>();
            foreach(var aiMat in aiScene.Materials)
            {
                string name;
                if (matNameTracker.ContainsKey(aiMat.Name))
                {
                    name = $"{aiMat.Name} ({matNameTracker[aiMat.Name]})";
                    matNameTracker[aiMat.Name] += 1;
                } else
                {
                    name = aiMat.Name;
                    matNameTracker.Add(aiMat.Name, 1);
                }

                AquaObject.GenericMaterial genMat = new AquaObject.GenericMaterial();
                List<string> shaderList = new List<string>();
                AquaObjectMethods.GetMaterialNameData(ref name, shaderList, out string alphaType, out string playerFlag);
                genMat.matName = name;
                genMat.shaderNames = shaderList;
                genMat.blendType = alphaType;
                genMat.specialType = playerFlag;

                //Texture assignments. Since we can't rely on these to export properly, we dummy them or just put diffuse if a playerFlag isn't defined.
                //We'll have the user set these later if needed.
                if(genMat.specialType != null)
                {
                    AquaObjectMethods.GenerateSpecialMaterialParameters(genMat);
                } else if(aiMat.TextureDiffuse.FilePath != null)
                {
                    genMat.texNames.Add(Path.GetFileName(aiMat.TextureDiffuse.FilePath));
                } else
                {
                    genMat.texNames.Add("tex0_d.dds");
                }
                genMat.texUVSets.Add(0); 

                AquaObjectMethods.GenerateMaterial(aqp, genMat, true);
            }

            //Default to this so ids can be assigned by order if needed
            Dictionary<string, int> boneDict = new Dictionary<string, int>();
            if(aiScene.RootNode.Name == null || !aiScene.RootNode.Name.Contains("(") || preAssignNodeIds == true)
            {
                int nodeCounter = 0;
                BuildAiNodeDictionary(aiScene.RootNode, ref nodeCounter, boneDict);
            }

            IterateAiNodesAQP(aqp, aqn, aiScene, aiScene.RootNode, Matrix4x4.Transpose(GetMat4FromAssimpMat4(aiScene.RootNode.Transform)), baseScale);
        }

        private static void BuildAiNodeDictionary(Assimp.Node aiNode, ref int nodeCounter, Dictionary<string, int> boneDict, bool useNameNodeNum = false)
        {
            var nodeCountName = GetNodeNumber(aiNode.Name);
            if (nodeCountName != -1)
            {
                useNameNodeNum = true;
                boneDict.Add(aiNode.Name, nodeCountName);
            } else if(useNameNodeNum == false)
            {
                boneDict.Add(aiNode.Name, nodeCounter);
            }

            //We can in theory ignore bones that don't meet either condition since they'll be effect nodes and listed outside the normal count
            nodeCounter++;

            foreach (var childNode in aiNode.Children)
            {
                BuildAiNodeDictionary(childNode, ref nodeCounter, boneDict, useNameNodeNum);
            }
        }

        private static void IterateAiNodesAQP(AquaObject aqp, AquaNode aqn, Assimp.Scene aiScene, Assimp.Node aiNode, Matrix4x4 parentTfm, float baseScale)
        {
            //Decide if this is an effect node or not
            string nodeName = aiNode.Name;
            var nodeParent = aiNode.Parent;
            if(ParseNodeId(nodeName, out int nodeId))
            {
                AquaNode.NODE node = new AquaNode.NODE();
                node.animatedFlag = 1;
                node.unkNode = -1;
                node.firstChild = -1;
                node.nextSibling = -1;
                node.const0_2 = 0;
                node.ngsSibling = 0; //Unsure how this is truly set. Seems to correlate to an id or bone count subtracted from 0xFFFFFFFF. However 0 seems to work so we just leave it as that.

                //If there's a parent, do things reliant on that
                if (nodeParent != null && ParseNodeId(nodeParent.Name, out int parNodeId))
                {
                    node.parentId = parNodeId;

                    //Fix up parent node associations
                    if(aqn.nodeList[parNodeId].firstChild == -1 || (aqn.nodeList[parNodeId].firstChild > nodeId))
                    {
                        var parNode = aqn.nodeList[parNodeId];
                        parNode.firstChild = nodeId;
                        aqn.nodeList[parNodeId] = parNode;
                    }

                    //Set next sibling. We loop through the parent node's children and set the smallest id that's larger than the present node's id. If nothing is found, keep it -1.
                    foreach(var childNode in nodeParent.Children)
                    {
                        if(childNode.Name != aiNode.Name)
                        {
                            ParseNodeId(childNode.Name, out int sibCandidate);
                            if (sibCandidate > nodeId && (node.nextSibling == -1 || sibCandidate < node.nextSibling))
                            {
                                node.nextSibling = sibCandidate;
                            }
                        }
                    }
                }
                else
                {
                    throw new Exception("Error: Parent node not processed before its child");
                }
                ParseShorts(nodeName, out node.boneShort1, out node.boneShort2);

                //Assign transform data ===TODO===
               // var worldMat = GetWorldMatrix(aiNode);

                //Put in  list at appropriate id 
                if (aqn.nodeList.Count < nodeId + 1)
                {
                    while(aqn.nodeList.Count < nodeId + 1)
                    {
                        aqn.nodeList.Add(new AquaNode.NODE());
                    }
                }
                aqn.nodeList[nodeId] = node;
            }
            else
            {
                if(aiNode.HasChildren)
                {
                    throw new Exception("Error: Effect nodes CANNOT have children. Add an id to the name to treat it as a standard node instead.");
                }
                AquaNode.NODO nodo = new AquaNode.NODO();
                nodo.animatedFlag = 1;
                nodo.boneName.SetString(nodeName);
                if(ParseNodeId(nodeParent.Name, out int parNodeId))
                {
                    nodo.parentId = parNodeId;
                } else
                {
                    throw new Exception("Error: Parent node not processed before its child");
                }
                ParseShorts(nodeName, out nodo.boneShort1, out nodo.boneShort2);

                //Assign transform data ===TODO===
                var mat4 = GetMat4FromAssimpMat4(aiNode.Transform);
                nodo.pos = new Vector3(mat4.M14 * baseScale, mat4.M24 * baseScale, mat4.M34 * baseScale);

                //var worldMat = GetWorldMatrix(aiNode);
                //nodo.eulRot =

                //aqn.nodoList.Add(nodo);
            }

            Matrix4x4 nodeMat = Matrix4x4.Transpose(GetMat4FromAssimpMat4(aiNode.Transform));
            nodeMat = Matrix4x4.Multiply(nodeMat, parentTfm);

            foreach (int meshId in aiNode.MeshIndices)
            {
                var mesh = aiScene.Meshes[meshId];
                //AddAiMeshToAQP(aqp, mesh, nodeMat);
            }

            foreach (var childNode in aiNode.Children)
            {
                IterateAiNodesAQP(aqp, aqn, aiScene, childNode, nodeMat, baseScale);
            }
        }

        public static void ParseShorts(string nodeName, out ushort boneShort1, out ushort boneShort2)
        {
            boneShort1 = 0x1C0;
            boneShort2 = 0;
            var numParse = nodeName.Split('#');
            if(numParse.Length > 1)
            {
                numParse[1] = numParse[1].Replace("0x", "");
                try
                {
                    boneShort1 = ushort.Parse(numParse[1], System.Globalization.NumberStyles.HexNumber);
                    if (numParse.Length > 2)
                    {
                        numParse[2] = numParse[1].Replace("0x", "");

                        try
                        {
                            boneShort2 = ushort.Parse(numParse[2], System.Globalization.NumberStyles.HexNumber);
                        }
                        catch
                        {
                        }
                    }
                } catch
                {
                }
            }
        }

        public static bool ParseNodeId(string nodeName, out int id)
        {
            var numParse = nodeName.Split('(', ')');
            if(Int32.TryParse(numParse[1], out int result))
            {
                id = result;
                return true;
            } else
            {
                id = -1;
                return false;
            }
        }

        public static Matrix4x4 GetMat4FromAssimpMat4(Assimp.Matrix4x4 mat4)
        {
            return new Matrix4x4(mat4.A1, mat4.A2, mat4.A3, mat4.A4,
                                 mat4.B1, mat4.B2, mat4.B3, mat4.B4,
                                 mat4.C1, mat4.C2, mat4.C3, mat4.C4,
                                 mat4.D1, mat4.D2, mat4.D3, mat4.D4);
        }
    }
}
