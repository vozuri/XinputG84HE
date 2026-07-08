# XinputG84HE

A lightweight, open-source Windows utility designed specifically for the EPOMAKER G84HE. This software maps your keyboard and HID inputs to virtual XInput gamepad commands, allowing you to use your keyboard as a controller. 

*Note: This project was 99% vibecoded by Claude Sonnet 5.*

### The Wooting 80HE Killer

In my opinion, the only **main** feature advantage the Wooting 80HE held over the competition was its native Xbox controller emulation. The EPOMAKER G84HE already has incredible hardware and Hall Effect switches, and this software bridges the final gap. By solving the controller emulation issue, this utility effectively makes the G84HE the 80HE killer.

---

### Features

* Maps keyboard keys onto a virtual Xbox 360 controller.
* Hotkey support for stopping emulation.
* Preset support for different mappings (e.g., driving games: W and S for triggers; other configurations: W and S for thumbstick).
* Anti Ghost Input option for when the keyboard may not update a key's switch status correctly.
* Blocked mapped keys option to mute keyboard switches used for current controller emulation.
* Deadzone customization.
* Force analog override command if the keyboard isn't sending data correctly.
* Live key actuation status (shows ghost inputs being nullified).

---

### Requirements

Before installing XinputG84HE, your system must have the ViGEmBus Driver installed. This is the framework that allows Windows to recognize the virtual Xbox controller.

1. Download the latest ViGEmBus installer from its official repository or trusted source.
2. Run the installer and restart your computer if prompted.

---

### Installation

1. Navigate to the Releases tab on the right side of this GitHub repository.
2. Download the latest installation file.
3. Run the .exe installer.
4. Follow the on-screen prompts to complete the installation.
5. Launch the application and begin mapping your keys.

---

### Open Source & Contributing

This project is fully open-source. If you want to review the code, suggest improvements, or compile it yourself, you are welcome to fork the repository. 

To compile from source:
* Clone this repository to your local machine.
* Open the project in Visual Studio.
* Ensure you have the necessary .NET SDK and WPF workloads installed.
* Build the solution.
