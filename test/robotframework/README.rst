cypnode robot framework tests
=============================

The cypnode robot framework tests are a collection of test suites for ``cypnode`` that use `Robot Framework <https://robotframework.org/>`_.

All tests are defined in `reStructuredText format <https://robotframework.org/robotframework/latest/RobotFrameworkUserGuide.html#restructuredtext-format>`_ and are stored in the respective test-type subfolder:

Smoke tests
-----------
Smoke tests cover the most basic of all functionalities, so that a broken build can quickly be found and fixed. These tests are part of the regular `Continuous Integration <https://en.wikipedia.org/wiki/Continuous_integration/>`_ process for Tangram software components, are automatically executed on each code change.

Location: ``./smoke``


Integration tests
-----------------
A combination of features is tested in a running system, so that broken features are found. It also helps development in analyzing parts of the behaviour of Tangram's software components without analyzing the full system.

Location: ``./integration``


Validation tests
----------------
These tests verify whether the complete Tangram software component fulfills the specification. For ``cypnode`` this means that the full lifecycle of a node works as intended: finding peers, synchronizing with the chain, reaching concensus, etc.

Location ``./validation``

Getting started
---------------
1. Install `python <https://www.python.org/>`_
2. Install `python-pip <https://pip.pypa.io/en/stable/>`_
3. Install `pipenv <https://pypi.org/project/pipenv/>`_
4. ``pipenv install``
5. ``pipenv run robot ./smoke/process.rst``

See ``./smoke/process.rst`` for configuration options.

Debian-based Linux distributions
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
1. ``sudo apt update``
2. ``sudo apt install python3 python3-pip pipenv``
