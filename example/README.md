        __ _____  ___ ___    _    _       _ _     _______        _      _____ _      _____
       / /| ____|/ _ \__ \  | |  | |     (_) |   |__   __|      | |    / ____| |    |_   _|
      / /_| |__ | | | | ) | | |  | |_ __  _| |_     | | ___  ___| |_  | |    | |      | |
     | '_ \___ \| | | |/ /  | |  | | '_ \| | __|    | |/ _ \/ __| __| | |    | |      | |
     | (_) |__) | |_| / /_  | |__| | | | | | |_     | |  __/\__ \ |_  | |____| |____ _| |_
      \___/____/ \___/____|  \____/|_| |_|_|\__|    |_|\___||___/\__|  \_____|______|_____|

                                          c64lib examples

#### Introduction

This directory contains a decent sized test suite on my c64lib project (https://github.com/barryw/c64lib). This library is a suite of functions for sprites, timers, memory management, joystick/keyboard reading, etc.

The 6502 Unit Test CLI is run as a Docker container, so you will need to have Docker running.

Run the tests by running `make` in this directory. The test definitions are in `tests.yaml` and the code under test is `include_me_full.prg`
