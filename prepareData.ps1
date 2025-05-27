param(
    [string]$InputFile = "testdata.txt",
    [string]$OutputFile = "parsed.csv"
)

# Write CSV header
"time,level,runtime,statuscode,failpoint" | Out-File -Encoding utf8 $OutputFile

Get-Content $InputFile | ForEach-Object {
    $line = $_
    if ($line -notmatch '^time=') { return }

    # Extract time, level, msg fields
    $time = ""
    $level = ""
    $msg = ""

    if ($line -match 'time="([^"]+)"') { $time = $matches[1] }
    if ($line -match 'level=([^\s]+)') { $level = $matches[1] }
    if ($line -match 'msg="((?:[^"\\]|\\.)*)"') { $msg = $matches[1] }

    # Try to parse msg as CSV: runtime,statuscode,failpoint
    if ($msg -match '^(\d+),(\d+),([^,]*)$') {
        $runtime = $matches[1]
        $statuscode = $matches[2]
        $failpoint = $matches[3]
        "$time,$level,$runtime,$statuscode,$failpoint" | Add-Content -Encoding utf8 $OutputFile
    }
    else {
        # Failure case: leave runtime and failpoint blank, put msg in failpoint
        $statuscode = ""
        $runtime = ""
        $failpoint = $msg -replace '"','""'
        "$time,$level,,$statuscode,""$failpoint""" | Add-Content -Encoding utf8 $OutputFile
    }
}
