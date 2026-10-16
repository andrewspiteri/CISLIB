"""Independent expectations for final startup state from unchanged shared vectors."""
import ntpath
import posixpath


def expected_reads(settings, environment):
    names = []
    for setting in settings:
        key = setting["key"]
        names.append(key)
        if not (environment.get(key) or "").strip():
            names.extend(setting.get("aliases", []))
    return names


def check_resolution(row, actual, settings, directory, family):
    expected = row["expected"]
    if expected["hasParseErrors"]:
        keys = [setting["key"] for setting in expected["settings"] if setting["state"] == "InvalidToken"]
        assert actual["status"] == "Invalid" and "values" not in actual
        assert [(issue["code"], issue["key"]) for issue in actual["issues"]] == [("InvalidToken", key) for key in keys]
        assert actual["filePath"] is None and actual["workingDirectory"] is None
    else:
        assert actual["status"] == "Valid" and not actual["issues"]
        assert actual["values"]["BuildVersion"] == expected["buildVersion"]
        assert actual["consoleEnabled"] == expected["consoleEnabled"]
        assert actual["fileEnabled"] == expected["fileEnabled"]
        properties = {setting["key"]: setting["property"] for setting in settings}
        native = ntpath if family == "windows" else posixpath
        for setting in expected["settings"]:
            wanted = setting["value"]
            if setting["key"] == "CORE_LOG_FILE_PATH" and wanted is not None:
                wanted = native.normpath(native.join(directory, wanted))
            assert actual["values"][properties[setting["key"]]] == wanted, row["id"] + ":" + setting["key"]
