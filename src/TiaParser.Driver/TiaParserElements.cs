using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TiaParser.Driver
{
    public class TiaParserElements
    {
        public interface IBlockElement { }

        public class ElementBlock
        {
            public int Size { get; set; }
            public string ID { get; set; }
            public string Name { get; set; }
            public int Offset { get; set; }
            public int Address { get; set; }
            public TiaBlock.TiaBlockType BlockType { get; set; }
        }

        public class Root : IBlockElement
        {
            public ElementBlock Block { get; set; }
            public string InterfaceGuid { get; set; }
            public string RIdSlots { get; set; }
            public List<MemberItem> Items { get; set; } = new List<MemberItem>();
            public Offsets RootOffsets { get; set; }
            public ExtensionMemory ExtensionMemory { get; set; }
            public External External { get; set; }
        }

        public class Member : IBlockElement
        {
            public ElementBlock Block { get; set; }
            public string ParentId { get; set; }
            public List<Offsets> Offsets { get; set; } = new List<Offsets>();
            public List<MemberItem> Items { get; set; } = new List<MemberItem>();
        }

        public class MemberItem
        {
            public string ID { get; set; }
            public string Name { get; set; }
            public string RID { get; set; }
            public string StdO { get; set; }
            public string LID { get; set; }
            public string V { get; set; }
            public string SubPartIndex { get; set; }
            public string DataType { get; set; }
            public string MFlags { get; set; }
            public List<MemberItem> Items { get; set; } = new List<MemberItem>();
        }

        public class Offsets
        {
            public string StdSize { get; set; }
            public string OptSize { get; set; }
            public string Flags { get; set; }
            public string CRC { get; set; }
            public string VolSize { get; set; } // Only for <Root>
            public ParamSize ParamSize { get; set; } // Only for <Root>
            public List<string> OValues { get; set; } = new List<string>();
        }

        public class ParamSize
        {
            public string StdSize { get; set; }
            public string VolSize { get; set; }
            public string VolFlags { get; set; }
            public string AllFlags { get; set; }
        }

        public class ExtensionMemory
        {
            public string VolatileSize { get; set; }
        }

        public class External
        {
            public int MultiFBCount { get; set; }
            public List<ExternalType> ExternalTypes { get; set; } = new List<ExternalType>();
        }

        public class ExternalType
        {
            public int SubPartIndex { get; set; }
            public string Type { get; set; }
            public string BlockClass { get; set; }
            public List<Usage> Usages { get; set; } = new List<Usage>();
        }

        public class Usage
        {
            public string Path { get; set; }
            public string Name { get; set; }
            public string VolStart { get; set; }
            public string Section { get; set; }
        }

#nullable disable
        /// <summary>
        /// Parses the XML data for each element block.
        /// It distinguishes between "Root" and "Member" blocks, processing their specific XML content.
        /// Additionally, maps any instance blocks to the appropriate reference blocks.
        /// </summary>
        public static void ParseXmlData(List<TiaElementBlockData> xmlElementBlocks)
        {
            foreach (TiaElementBlockData xmlElementBlock in xmlElementBlocks)
            {
                // When the XmlBlock is null it means that the
                if (xmlElementBlock.XmlBlock != null)
                {
                    try
                    {
                        if (xmlElementBlock.XmlBlock.XmlData?.Root?.Name == "Root")
                        {
                            ParseRootXmlData(xmlElementBlock);
                        }
                        else if (xmlElementBlock.XmlBlock.XmlData?.Root?.Name == "Member")
                        {
                            ParseMemberXmlData(xmlElementBlock);
                        }
                    }
                    catch (Exception exception)
                    {
                        TiaParserDriver.Logger.Warn(
                            exception,
                            $"FAILED PARSING XML {xmlElementBlock.Block.Offset}"
                        );
                    }
                }
            }

            MapInstanceBlocks(xmlElementBlocks);
        }

        /// <summary>
        /// Maps instance blocks to their reference blocks.
        /// If the XML block for an instance element is missing, it attempts to find the matching reference block
        /// and assigns the XML block from that reference.
        /// </summary>
        private static void MapInstanceBlocks(List<TiaElementBlockData> xmlElementBlocks)
        {
            // Resolve XmlBlocks for instance elements that the XML block is belongs to a ReferenceBlock
            foreach (
                TiaElementBlockData instanceElementBlock in xmlElementBlocks.Where(block =>
                    block.XmlBlock == null
                )
            )
            {
                bool matchSucess = false;

                foreach (TiaElementBlockData referenceBlock in xmlElementBlocks)
                {
                    if (instanceElementBlock.ReferenceBlock == referenceBlock.Name)
                    {
                        matchSucess = true;
                        instanceElementBlock.XmlBlock = referenceBlock.XmlBlock;
                    }
                }

                if (!matchSucess)
                {
                    TiaParserDriver.Logger.Warn(
                        $"FAILED MAPPING XML BLOCK WITH ELEMENT {instanceElementBlock.Name} - {instanceElementBlock.DataOffset}"
                    );
                }
            }
        }

        /// <summary>
        /// Processes XML data for a "Root" block.
        /// Extracts root-specific data like offsets, interface GUID, external memory, and external types.
        /// It also processes any member elements found in the root.
        /// </summary>
        private static void ParseRootXmlData(TiaElementBlockData xmlElementBlock)
        {
            Root root = (Root)xmlElementBlock.XmlBlock.BlockElement;

            if (xmlElementBlock.XmlBlock.XmlData.Root.Attribute("InterfaceGuid") != null)
            {
                root.InterfaceGuid = xmlElementBlock
                    .XmlBlock.XmlData.Root.Attribute("InterfaceGuid")
                    .Value;

                root.RIdSlots = xmlElementBlock
                    .XmlBlock.XmlData.Root.Attribute("InterfaceGuid")
                    ?.Value;
            }

            foreach (
                XElement memberElement in xmlElementBlock.XmlBlock.XmlData.Root.Descendants(
                    "Member"
                )
            )
            {
                root.Items.Add(ProcessItem(memberElement));
            }

            XElement offsetsElement = xmlElementBlock.XmlBlock.XmlData.Root.Element("Offsets");
            if (offsetsElement != null)
            {
                root.RootOffsets = ProcessOffsets(offsetsElement, true);
            }

            XElement extensionMemoryElement = xmlElementBlock.XmlBlock.XmlData.Root.Element(
                "ExtensionMemory"
            );
            if (extensionMemoryElement != null)
            {
                root.ExtensionMemory = new ExtensionMemory
                {
                    VolatileSize = extensionMemoryElement.Attribute("VolatileSize")?.Value
                };
            }

            XElement externals = xmlElementBlock.XmlBlock.XmlData.Root.Element("Externals");
            if (externals != null)
            {
                External parsedExternal = ParseExternals(externals);

                root.External = parsedExternal;
            }

            xmlElementBlock.XmlBlock.BlockElement = root;
        }

        /// <summary>
        /// Processes XML data for a "Member" block.
        /// Extracts member-specific attributes such as ParentId and processes member offsets and items.
        /// </summary>
        private static void ParseMemberXmlData(TiaElementBlockData xmlElementBlock)
        {
            Member member = (Member)xmlElementBlock.XmlBlock.BlockElement;

            if (xmlElementBlock.XmlBlock.XmlData.Root.Attribute("ParentId") == null)
            {
                member.ParentId = "InternalSection";
            }
            else
            {
                member.ParentId = xmlElementBlock.XmlBlock.XmlData.Root.Attribute("ParentId").Value;
            }

            foreach (
                XElement offsetsElement in xmlElementBlock.XmlBlock.XmlData.Root.Descendants(
                    "Offsets"
                )
            )
            {
                member.Offsets.Add(ProcessOffsets(offsetsElement, false));
            }

            foreach (
                XElement memberElement in xmlElementBlock.XmlBlock.XmlData.Root.Descendants(
                    "Member"
                )
            )
            {
                member.Items.Add(ProcessItem(memberElement));
            }

            xmlElementBlock.XmlBlock.BlockElement = member;
        }

        /// <summary>
        /// Processes an external block element and extracts its attributes.
        /// This includes handling multiple external types and their associated usages, as well as multi-function block counts.
        /// </summary>
        public static External ParseExternals(XElement externalsElement)
        {
            var external = new External
            {
                MultiFBCount = int.Parse(externalsElement.Attribute("MultiFBCount")?.Value ?? "0")
            };

            foreach (XElement externalTypeElement in externalsElement.Elements("ExternalType"))
            {
                ExternalType externalType = new ExternalType
                {
                    SubPartIndex = int.Parse(
                        externalTypeElement.Attribute("SubPartIndex")?.Value ?? "0"
                    ),
                    Type = externalTypeElement.Attribute("Name")?.Value,
                    BlockClass = externalTypeElement.Attribute("BlockClass")?.Value
                };

                foreach (XElement usageElement in externalTypeElement.Elements("Usage"))
                {
                    string section = "Static";

                    if (usageElement.Attribute("Section") != null)
                    {
                        section = usageElement.Attribute("Section").Value;
                    }

                    Usage usage = new Usage
                    {
                        Path = usageElement.Attribute("Path")?.Value,
                        Name = usageElement.Attribute("Name")?.Value,
                        VolStart = usageElement.Attribute("volStart")?.Value,
                        Section = section
                    };
                    externalType.Usages.Add(usage);
                }

                external.ExternalTypes.Add(externalType);
            }

            return external;
        }

        /// <summary>
        /// Processes an XML member element and extracts its attributes into an Item object.
        /// Recursively processes nested members and populates them into the Item.
        /// </summary>
        private static MemberItem ProcessItem(XElement memberElement)
        {
            MemberItem item = new MemberItem
            {
                ID = memberElement.Attribute("ID")?.Value,
                Name = memberElement.Attribute("Name")?.Value,
                RID = memberElement.Attribute("RID")?.Value,
                DataType = memberElement.Attribute("Type")?.Value,
                SubPartIndex = memberElement.Attribute("SubPartIndex")?.Value,
                StdO = memberElement.Attribute("StdO")?.Value,
                LID = memberElement.Attribute("LID")?.Value,
                V = memberElement.Attribute("v")?.Value
            };

            // Recursively process nested members (if any)
            foreach (XElement childMember in memberElement.Elements("Member"))
            {
                item.Items.Add(ProcessItem(childMember));
            }

            return item;
        }

        /// <summary>
        /// Processes an XML element representing offsets and extracts relevant attributes into an Offsets object.
        /// Additional parameters are extracted for root elements, and all found offset values are collected.
        /// </summary>
        private static Offsets ProcessOffsets(XElement offsetsElement, bool isRoot)
        {
            Offsets offsets = new Offsets
            {
                StdSize = offsetsElement.Attribute("stdSize")?.Value,
                OptSize = offsetsElement.Attribute("optSize")?.Value,
                Flags = offsetsElement.Attribute("Flags")?.Value,
                CRC = offsetsElement.Attribute("CRC")?.Value
            };

            if (isRoot)
            {
                ParamSize paramSize = new ParamSize();
                offsets.VolSize = offsetsElement.Attribute("volSize")?.Value;
                XElement paramSizeElement = offsetsElement.Element("ParamSize");
                if (paramSizeElement != null)
                {
                    paramSize.StdSize = paramSizeElement.Attribute("stdSize")?.Value;
                    paramSize.VolSize = paramSizeElement.Attribute("volSize")?.Value;
                    paramSize.VolFlags = paramSizeElement.Attribute("volFlags")?.Value;
                    paramSize.AllFlags = paramSizeElement.Attribute("allFlags")?.Value;
                }
            }

            foreach (XElement offsetElement in offsetsElement.Descendants("o"))
            {
                string oValue = offsetElement.Attribute("o")?.Value;
                offsets.OValues.Add(oValue);
            }

            return offsets;
        }
    }
}
