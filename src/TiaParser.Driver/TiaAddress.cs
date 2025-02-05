namespace TiaParser.Driver
{
    public class TiaAddress
    {
        public TiaAddress() { }

        public TiaAddress(string name, string referenceAddress)
        {
            Name = name;
            ReferenceAddress = referenceAddress;
        }

        public string Name { get; set; }
        public string ReferenceAddress { get; set; }
        public List<TiaAddress> Addresses { get; private set; } = new List<TiaAddress>();

        /// <summary>
        /// Concatenates the current <see cref="TiaAddress"/> object's `Name` and `ReferenceAddress` with the provided `name` and `address`,
        /// creating a new <see cref="TiaAddress"/> object with the combined values.
        /// </summary>
        /// <param name="name">The additional name to concatenate.</param>
        /// <param name="address">The additional address to concatenate.</param>
        /// <returns>A new <see cref="TiaAddress"/> object with concatenated `Name` and `ReferenceAddress`.</returns>
        private TiaAddress ConcatItemAddress(string name, string address)
        {
            // Create a new TiaAddress with concatenated values
            return new TiaAddress(
                string.Concat(this.Name, $".{name}"),
                string.Concat(this.ReferenceAddress, $".{address}")
            );
        }

        /// <summary>
        /// Inserts the provided <see cref="TiaAddress"/> object into the internal list of addresses.
        /// </summary>
        /// <param name="tiaAddress">The <see cref="TiaAddress"/> object to add to the list.</param>
        private void InsertTiaAddress(TiaAddress tiaAddress)
        {
            this.Addresses.Add(tiaAddress);
        }

        /// <summary>
        /// Builds a list of <see cref="TiaAddress"/> objects based on the provided list of PLC blocks.
        /// For each block, it constructs an address and recursively processes its items, creating nested addresses.
        /// The method also converts numeric addresses to hexadecimal format and logs them.
        /// </summary>
        /// <param name="plcBlocks">A list of <see cref="PlcBlock"/> objects from which addresses will be built.</param>
        /// <returns>A list of built <see cref="TiaAddress"/> objects.</returns>
        public void BuildAddress(List<TiaPlcBlock> plcBlocks)
        {
            foreach (TiaPlcBlock block in plcBlocks)
            {
                // Create initial TiaAddress object for the block
                TiaAddress tiaAddress = new TiaAddress(block.Name, $"{block.Address}");

                InsertTiaAddress(tiaAddress);

                foreach (TiaPlcItem plcItem in block.PlcItems)
                {
                    try
                    {
                        if (plcItem.Address != null)
                        {
                            // Create a new address by concatenating item name and address
                            TiaAddress newAddress = tiaAddress.ConcatItemAddress(
                                plcItem.Name,
                                plcItem.Address
                            );

                            InsertTiaAddress(newAddress);

                            // Recursively build nested addresses
                            BuildItemAddress(plcItem, newAddress);
                        }
                    }
                    catch (Exception exception)
                    {
                        TiaParserDriver.Logger.Warn(
                            exception,
                            $"ERROR BUILDING ADDRESS {plcItem.Name} ID: {plcItem.Address} MSG: {exception.Message}"
                        );
                    }
                }
            }

            FormatAddress();
        }

        private void FormatAddress()
        {
            // Output the final addresses
            foreach (TiaAddress tiaAddress in Addresses)
            {
                string[] addresses = tiaAddress.ReferenceAddress.Split('.');

                for (int i = 0; i < addresses.Length; i++)
                {
                    if (int.TryParse(addresses[i], out int intAddress))
                    {
                        // Convert to hex
                        addresses[i] = $"{intAddress:X}";
                    }
                    else
                    {
                        TiaParserDriver.Logger.Debug($"Invalid address: {addresses[i]}");
                    }
                }

                tiaAddress.ReferenceAddress = $"8A0E{string.Join(".", addresses)}";
            }
        }

        /// <summary>
        /// Recursively builds and inserts addresses for nested PLC items under a parent <see cref="TiaAddress"/>.
        /// Each nested item has its name and address concatenated with the parent's values, creating new addresses.
        /// </summary>
        /// <param name="plcItem">The parent <see cref="TiaPlcItem"/> whose nested items will be processed.</param>
        /// <param name="parentAddress">The parent <see cref="TiaAddress"/> used as the base for the new addresses.</param>
        public void BuildItemAddress(TiaPlcItem plcItem, TiaAddress parentAddress)
        {
            foreach (TiaPlcItem nestedItem in plcItem.Items)
            {
                // Create a new address for each nested item
                TiaAddress newAddress = parentAddress.ConcatItemAddress(
                    nestedItem.Name,
                    nestedItem.Address
                );

                InsertTiaAddress(newAddress);

                // Recursively process further nested items
                BuildItemAddress(nestedItem, newAddress);
            }
        }
    }
}
