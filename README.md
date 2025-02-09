# Overview
This project is an ongoing effort to understand the structure of the `.plf` file format and extract valuable information from it. While significant progress has been made, the parser is currently reliable only for simple projects. Handling complex, real-world automation projects will require continued development to address edge cases and all possible variations the file may present.

# Compatibility
The application is supposed to work with TIA Portal versions 15 and onwards.

# Known Issues
Within the TIA Portal, there is a mechanism that runs a Reorganization Task to eliminate data from the `.plf` file once its size exceeds approximately 5 KB. Unfortunately, this mechanism seems may remove important information needed to reconstruct the data, which affects the parser’s ability to work with larger files.

# Tools Used
To read and analyze the `.plf` file, was used [Imhex](https://github.com/WerWolv/ImHex) by WerWolv, which I highly recommend.

# How to Use
 1. Navigate to the System folder in a typical TIA Portal project.
 2. Select the `PEData.plf` file.
 3. The reference addresses will be exported to the directory containing the `.plf` file in an `export.txt` file.
 <img width="500" alt="image" src="https://github.com/user-attachments/assets/cbfdc23a-edf8-440c-aea7-94e7a1b1f6e3" />

