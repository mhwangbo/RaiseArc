"""Deprecated launch path. Use raisearc_mcp.py; retained for existing client configurations."""
import importlib.util as _util
from pathlib import Path as _Path

_spec = _util.spec_from_file_location("raisearc_mcp", _Path(__file__).with_name("raisearc_mcp.py"))
_adapter = _util.module_from_spec(_spec)
_spec.loader.exec_module(_adapter)
globals().update({name: value for name, value in vars(_adapter).items() if not name.startswith("_")})

if __name__ == "__main__":
    main()
