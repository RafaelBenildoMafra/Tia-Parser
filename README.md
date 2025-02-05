# Overview
This project is an ongoing effort to understand the structure of the `.plf` file format and extract valuable information from it. While significant progress has been made, the parser is currently reliable only for simple projects. Handling complex, real-world automation projects will require continued development to address edge cases and all possible variations the file may present.

# Compatibility
The application is supposed to work with TIA Portal versions 15 and onwards.

# Known Issues
Within the TIA Portal, there is a mechanism that runs a Reorganization Task to eliminate data from the `.plf` file once its size exceeds approximately 5 KB. Unfortunately, this mechanism may remove important information needed to reconstruct the data, which affects the parser’s ability to work with larger files.

# Tools Used
To read and analyze the `.plf` file, I used [Imhex](https://github.com/WerWolv/ImHex) by WerWolv, which I highly recommend.

# How to Use
Navigate to the system folder in a typical TIA Portal project.
Select the `PEData.plf` file.
