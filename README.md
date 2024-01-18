# ratgdo for HS4

This is a free, open-source HomeSeer plugin that interfaces HS4 with the [ratgdo](https://paulwieland.github.io/ratgdo/)
device to enable full local control of Chamberlain and LiftMaster garage door openers.

# Installation

This plugin is available in the HomeSeer plugin updater.

If you'd like to install a new version early before it's approved for release in the plugin updater,
download the zip file starting with "ratgdo" from the [releases page](https://github.com/DoctorMcKay/HSPI_ratgdo/releases).
Extract the zip, then copy these files to these locations:

- HSPI_ratgdo.exe and HSPI_ratgdo.exe.config go in your HS4 installation folder
- All .dll files go in bin/ratgdo relative to your HS4 installation folder
- All .html files go in html/ratgdo relative to your HS4 installation folder
- Ignore install.txt; it's only used by the HS4 plugin updater
