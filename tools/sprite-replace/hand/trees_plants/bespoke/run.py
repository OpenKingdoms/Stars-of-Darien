"""Builds the named bespoke models in one Blender session.

    blender -b --factory-startup --python bespoke/run.py -- Name [Name ...] [--norender]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common  # noqa: E402

common.main()
