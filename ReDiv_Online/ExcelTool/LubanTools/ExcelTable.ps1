# Luban Excel 配置表（DataTables/Datas/*.xlsx）行级增查工具：通过 Excel COM 自动化编辑，
# 而不是用 openpyxl/文本方式直接改 .xlsx 二进制——本仓库的表里大量单元格是公式
#（如 iconName/NameKey/DescKey 用 =CONCATENATE(...)，枚举表 FunctionGroup 用 =J45*2 之类的自增值），
# 用不打开真正 Excel 的库改完保存后，公式的缓存结果会丢失，Luban（走 ExcelDataReader 只读缓存值,不重新计算公式）
# 读到的就是空值——所以必须让真正的 Excel 打开、写入、保存，由 Excel 自己重新计算并写回缓存值。
# 代价是本机必须装有 Microsoft Excel，且执行期间会短暂后台启动一个不可见的 EXCEL.EXE 进程。
#
# 表格约定（与现有 Datas/*.xlsx 一致）：每个 sheet 前 4 行为头部——
#   第1行 ##var   ：列变量名（表字段名，如 ID / Remark / ItemType...）
#   第2行 ##type  ：Luban 类型（long / string / int / float / 枚举名 / TbXxx#sep=+ ...）
#   第3行 ##group ：这一列给谁用 —— c=仅客户端 / s=仅服务端 / c,s=两端都有（留空同 c,s）
#   第4行 ##      ：中文列名注释
# 第5行起为数据行。
#
# ⚠️ **表头行号不要写死。** 有的老表只有 3 行（没有 ##group），__beans__ 那种元表还会
# 出现两行 ##var。凡是要读写表头的地方一律用 Get-HeaderRows 按标记定位 ——
# 2026-08-24 就是因为 AddColumn 写死了 1/2/3，把中文注释写进了 ##group 行。
# 新建表（AddSheet）一律建 4 行表头，给已有老表补 ##group 行用 -Action SetHeader。
#
# 顺带一提：本仓库的角色表 read_schema_from_file=True，所以 ##var/##type/##group/##
# 四行**就是 schema 本身**（字段名 / 类型 / 分组 / 注释全以它为准，不再写 XML bean）。
# 实测过：##group 的分组会真的把 c 列挡在服务端产物外、s 列挡在客户端产物外，
# ## 那行还会变成生成代码的 /// <summary>。所以这四行写错就是改错了表结构。
#
# __enums__.xlsx 的 Sheet1 稍特殊：##var 出现两行（第二行是 *items 子表头
# name/alias/value/comment/tags），且同一张 sheet 里首尾相接地堆了很多个枚举，每个枚举块的
# 第一行 B 列（full_name）写枚举名，该枚举后续项行 B 列留空，直到下一个枚举块的 B 列再次出现新名字。
#
# 用法（务必用 & 调用操作符直接调脚本，不要套一层 `powershell -File ...`，否则中文参数可能被中转乱码）：
#
#   1) 只读查看某个 sheet（不整份打开文件，省 token）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action Dump -Workbook <相对/绝对路径> -Sheet <sheet名> [-MaxRows N]
#      查看 __enums__.xlsx 里某一个枚举块：加 -EnumName <枚举名>（忽略 -MaxRows，把该枚举全部项打印出来）
#
#   2) 给已有 sheet 追加数据行（按 -File 指向的 JSON 数组，每项是 {"列变量名": 值, ...}，
#      未出现的列留空；出现了表里不存在的列名会报错并列出合法列名，防止手误）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action AddRows -Workbook <路径> -Sheet <sheet名> -File <JSON路径>
#
#   2b) 改已有数据行的某几个单元格（按主键列——##var 行的第一个字段列，通常是 Id——定位行，
#      -File JSON 数组每项形如 {"Id": 10001, "DescKey": "TestObj1Desc"}，只写出现的列，其余列不动；
#      主键找不到或列名写错都会报错且整份不保存）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action UpdateRows -Workbook <路径> -Sheet <sheet名> -File <JSON路径>
#
#   3) 给已有 sheet 末尾追加一列（表头各行一次写好；-Default 给已有数据行填初值，不传则留空。
#      表里有 ##group 行时 **-Group 必填**，免得又漏掉分组）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action AddColumn -Workbook <路径> -Sheet <sheet名> `
#          -Var <列变量名> -Type <Luban类型> -Group <c|s|c,s> -Comment <中文列名> [-Default <初值>]
#
#   4) 新建一个 sheet（-File JSON 形如
#      {"columns":[{"var":"ID","type":"long","group":"c,s","comment":"id"},...], "rows":[{...}, ...]}，
#      rows 可省略只建表头；column 的 group 可省略，省略等于 "c,s"）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action AddSheet -Workbook <路径> -Sheet <新sheet名> -File <JSON路径>
#
#   4b) 改已有 sheet 的表头（改分组 / 改注释；**这张表没有 ##group 行的话会自动插一行**，
#      所以给老表补分组行也用它。-File JSON 数组每项形如
#      {"var":"StartStar","group":"s","comment":"建角色时的初始星级"}，group/comment 各自可省）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action SetHeader -Workbook <路径> -Sheet <sheet名> -File <JSON路径>
#
#   5) 往 __enums__.xlsx 的某个已有枚举里追加枚举项（插入到该枚举块末尾、下一个枚举块之前，
#      不会打乱其它枚举；-File JSON 形如 [{"name":"Xxx","alias":"别名","value":3,"comment":"备注"}, ...]，
#      alias/comment/tags 可省略）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action AddEnumItems -Workbook <路径> -Sheet <sheet名> -EnumName <枚举名> -File <JSON路径>
#
#   6) 新建一个枚举（追加到 sheet 末尾；-File JSON 形如
#      {"fullName":"Xxx","flags":false,"unique":true,"items":[{"name":"A","alias":"...","value":1,"comment":"..."}, ...]}）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action AddEnumType -Workbook <路径> -Sheet <sheet名> -File <JSON路径>
#
#   7) 删数据行（按主键列定位，整行删除并上移；-Keys 用逗号分隔多个主键值。
#      任何一个主键找不到都整份不保存，避免"删了一半"）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action DeleteRows -Workbook <路径> -Sheet <sheet名> -Keys "a,b"
#
#   8) 删整列（按 ##var 列变量名定位，整列删除并左移）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action DeleteColumn -Workbook <路径> -Sheet <sheet名> -Var <列变量名>
#
#   9) 删整个 sheet（工作簿里最后一个 sheet 删不掉，Excel 不允许）：
#      & ExcelTool/LubanTools/ExcelTable.ps1 -Action DeleteSheet -Workbook <路径> -Sheet <sheet名>
#
# 注意：
#   - AddSheet 的 -Workbook 指向不存在的文件时会**新建工作簿**（其余 Action 一律要求文件已存在）。
#   - 改完表后仍需照常跑一遍 DataTables/gen_client.bat（或对应 gen 脚本）才会生成/更新代码与 json 数据。
#   - 字符串列一律按文本写入（不会被 Excel 自动转成数字/日期）；数字列请在 JSON 里写数字类型。
#   - JSON 文件请用 UTF-8 保存（含中文没问题），脚本用 -Encoding UTF8 显式读取，不依赖 BOM 判断。

param(
    [Parameter(Mandatory = $true)][ValidateSet('Dump', 'AddRows', 'UpdateRows', 'AddColumn', 'AddSheet', 'AddEnumItems', 'AddEnumType', 'DeleteRows', 'DeleteColumn', 'DeleteSheet', 'SetHeader')][string]$Action,
    [Parameter(Mandatory = $true)][string]$Workbook,
    [Parameter(Mandatory = $true)][string]$Sheet,
    [string]$EnumName,
    [int]$MaxRows = 0,
    [string]$File,
    [string]$Var,
    [string]$Type,
    [string]$Comment,
    [string]$Default,
    [string]$Keys,
    [string]$Group
)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot([string]$startDir) {
    $dir = $startDir
    while ($dir) {
        if ((Test-Path (Join-Path $dir 'Assets')) -or (Test-Path (Join-Path $dir '.git'))) { return $dir }
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    return $startDir
}
$RepoRoot = Find-RepoRoot $PSScriptRoot

function Resolve-RepoPath([string]$path, [switch]$AllowMissing) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    $resolved = Join-Path $RepoRoot $path
    if (-not $AllowMissing -and -not (Test-Path $resolved)) { Write-Error "找不到文件：$resolved（相对路径基准目录：$RepoRoot）" }
    return $resolved
}

function Release-Com($obj) {
    if ($null -ne $obj) {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($obj)
    }
}

# 找出各个表头行的行号，返回 [标记 -> 行号]，比如 @{'##var'=1; '##type'=2; '##group'=3; '##'=4}。
#
# 表头行数**不固定**：数据表一般是 4 行（##var / ##type / ##group / ##），
# 但也有只写 3 行（没有 ##group）的老表，__beans__ 那种元表还会出现两行 ##var。
# 所以凡是要往表头里写东西的地方，一律用这个函数定位，**不要写死行号** ——
# 写死过一次，结果把中文注释写进了 ##group 行（2026-08-24 踩的）。
# 同一个标记出现多次时取第一次出现的那行。
function Get-HeaderRows($ws) {
    $rows = @{}
    $r = 1
    while ($true) {
        $v = $ws.Cells.Item($r, 1).Value2
        if ($null -eq $v -or -not ("$v".StartsWith('##'))) { return $rows }
        $key = "$v".Trim()
        if (-not $rows.ContainsKey($key)) { $rows[$key] = $r }
        $r++
        if ($r -gt 20) { throw "sheet「$($ws.Name)」表头超过20行未找到数据起始行，格式可能不对。" }
    }
}

# 找 sheet 的表头行数：从第1行开始，A 列值以 "##" 开头的行都算表头，第一条不是 "##" 开头的行即数据起始行。
function Get-DataStartRow($ws) {
    $r = 1
    while ($true) {
        $v = $ws.Cells.Item($r, 1).Value2
        if ($null -eq $v -or -not ("$v".StartsWith('##'))) { return $r }
        $r++
        if ($r -gt 20) { throw "sheet「$($ws.Name)」表头超过20行未找到数据起始行，格式可能不对。" }
    }
}

# 读 ##var 行（表头第1行），返回 [列变量名 -> 列号] 的顺序表（跳过 None/空列）。
function Get-VarColumns($ws, [int]$varRow, [int]$lastCol) {
    $cols = @()
    for ($c = 1; $c -le $lastCol; $c++) {
        $v = $ws.Cells.Item($varRow, $c).Value2
        if ($null -ne $v -and "$v".Trim() -ne '' -and -not ("$v".StartsWith('##'))) {
            $cols += [PSCustomObject]@{ Name = "$v".Trim(); Col = $c }
        }
    }
    return $cols
}

function Get-UsedExtent($ws) {
    $ur = $ws.UsedRange
    $lastRow = $ur.Row + $ur.Rows.Count - 1
    $lastCol = $ur.Column + $ur.Columns.Count - 1
    Release-Com $ur
    return [PSCustomObject]@{ LastRow = $lastRow; LastCol = $lastCol }
}

# 把一个 PS 值写进单元格：数字类型走 .Value2（Excel 按数字存），其余一律按纯文本写（NumberFormat=@），
# 避免形如 "1E5"、纯数字字符串被 Excel 自动转成数字/科学计数法/日期。
function Set-CellValue($ws, [int]$row, [int]$col, $value) {
    $cell = $ws.Cells.Item($row, $col)
    if ($null -eq $value) {
        $cell.Value2 = ''
    }
    elseif ($value -is [double] -or $value -is [int] -or $value -is [long] -or $value -is [int64] -or $value -is [decimal]) {
        # 数字不显式设置 NumberFormat='General'：中文版 Excel 通过 COM 设置该字符串会抛
        # "不能设置类 Range 的 NumberFormat 属性"（本地化格式码不认英文关键字），新单元格默认已是通用格式，直接赋值即可。
        $cell.Value2 = [double]$value
    }
    elseif ($value -is [bool]) {
        $cell.Value2 = if ($value) { 'TRUE' } else { 'FALSE' }
    }
    else {
        $cell.NumberFormat = '@'
        $cell.Value2 = "$value"
    }
    Release-Com $cell
}

function Read-JsonFile([string]$path) {
    $full = Resolve-RepoPath $path
    $text = Get-Content -LiteralPath $full -Raw -Encoding UTF8
    return ConvertFrom-Json $text
}

# AddSheet 允许指向还不存在的文件（那就是"新建一张配置表"），其余 Action 都要求文件已在。
$fullWorkbook = Resolve-RepoPath $Workbook -AllowMissing:($Action -eq 'AddSheet')
$creatingWorkbook = -not (Test-Path $fullWorkbook)
if ($creatingWorkbook -and $Action -ne 'AddSheet') { Write-Error "找不到文件：$fullWorkbook" }

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$excel.AskToUpdateLinks = $false
$wb = $null
$succeeded = $false
try {
    if ($creatingWorkbook) {
        # 先 SaveAs 落盘，后面 finally 里的 $wb.Save() 才不会弹"另存为"对话框卡死无人值守的调用。
        $wb = $excel.Workbooks.Add()
        while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }
        $wb.SaveAs($fullWorkbook, 51)  # 51 = xlOpenXMLWorkbook (.xlsx)
    }
    else {
        $wb = $excel.Workbooks.Open($fullWorkbook, [Type]::Missing, $false)
    }

    if ($Action -eq 'AddSheet') {
        $exists = $false
        foreach ($s in $wb.Worksheets) { if ($s.Name -eq $Sheet) { $exists = $true }; Release-Com $s }
        if ($exists) { throw "sheet「$Sheet」已存在于 $(Split-Path -Leaf $fullWorkbook)，未创建。" }

        $spec = Read-JsonFile $File
        if ($creatingWorkbook) {
            # 新建的工作簿里那张默认空表直接改名用掉，免得留一张空 sheet 让 Luban 读到空表头报错。
            $ws = $wb.Worksheets.Item(1)
        }
        else {
            $ws = $wb.Worksheets.Add([Type]::Missing, $wb.Worksheets.Item($wb.Worksheets.Count))
        }
        $ws.Name = $Sheet

        # 第1列（A列）本身不是字段，Luban 约定用来放 ##var/##type/##group/## 标记；
        # 真正的字段列从第2列（B列）起。表头**一律建 4 行**，##group 不能省 ——
        # 有了它就不用在每条注释里写「仅客户端 / 仅服务端」了。
        Set-CellValue $ws 1 1 '##var'
        Set-CellValue $ws 2 1 '##type'
        Set-CellValue $ws 3 1 '##group'
        Set-CellValue $ws 4 1 '##'

        $c = 2
        foreach ($col in $spec.columns) {
            Set-CellValue $ws 1 $c $col.var
            Set-CellValue $ws 2 $c $col.type
            # 不写 group 就当两端都要（和 Luban「不标 group = 属于所有分组」一致）
            $g = if ($col.PSObject.Properties['group']) { $col.group } else { 'c,s' }
            Set-CellValue $ws 3 $c $g
            Set-CellValue $ws 4 $c $col.comment
            $c++
        }

        $r = 5
        foreach ($row in $spec.rows) {
            $c = 2
            foreach ($col in $spec.columns) {
                $prop = $row.PSObject.Properties[$col.var]
                $val = $null
                if ($prop) { $val = $prop.Value }
                Set-CellValue $ws $r $c $val
                $c++
            }
            $r++
        }
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已在 $(Split-Path -Leaf $fullWorkbook) 新建 sheet「$Sheet」，$($spec.columns.Count) 列，$($spec.rows.Count) 行数据。"
    }
    elseif ($Action -eq 'AddRows') {
        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $varRow = 1
        $cols = Get-VarColumns $ws $varRow $ext.LastCol
        $dataStart = Get-DataStartRow $ws

        $newRows = Read-JsonFile $File
        $validNames = $cols.Name
        $r = $ext.LastRow + 1
        if ($r -lt $dataStart) { $r = $dataStart }
        $count = 0
        foreach ($row in $newRows) {
            foreach ($p in $row.PSObject.Properties) {
                if ($validNames -notcontains $p.Name) {
                    throw "列「$($p.Name)」不存在于 sheet「$Sheet」，合法列名：$($validNames -join ', ')"
                }
            }
            foreach ($col in $cols) {
                $prop = $row.PSObject.Properties[$col.Name]
                $val = $null
                if ($prop) { $val = $prop.Value }
                Set-CellValue $ws $r $col.Col $val
            }
            $r++
            $count++
        }
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已向 $(Split-Path -Leaf $fullWorkbook) 的 sheet「$Sheet」追加 $count 行（从第 $($ext.LastRow + 1) 行起）。"
    }
    elseif ($Action -eq 'UpdateRows') {
        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $cols = Get-VarColumns $ws 1 $ext.LastCol
        $dataStart = Get-DataStartRow $ws

        # 主键 = ##var 行的第一个字段列（本仓库的表一律是 Id）
        $keyName = $cols[0].Name
        $keyCol = $cols[0].Col

        $rowByKey = @{}
        for ($r = $dataStart; $r -le $ext.LastRow; $r++) {
            $v = $ws.Cells.Item($r, $keyCol).Value2
            if ($null -eq $v -or "$v".Trim() -eq '') { continue }
            $rowByKey["$v".Trim()] = $r
        }

        $updates = Read-JsonFile $File
        $validNames = $cols.Name
        $rowCount = 0
        $cellCount = 0
        foreach ($row in $updates) {
            $keyProp = $row.PSObject.Properties[$keyName]
            if (-not $keyProp) { throw "每项都要带主键列「$keyName」用来定位行，未改动。" }
            $key = "$($keyProp.Value)".Trim()
            if (-not $rowByKey.ContainsKey($key)) {
                throw "sheet「$Sheet」里找不到 $keyName = $key 的数据行，未改动。"
            }
            $target = $rowByKey[$key]
            foreach ($p in $row.PSObject.Properties) {
                if ($p.Name -eq $keyName) { continue }
                $col = $cols | Where-Object { $_.Name -eq $p.Name }
                if (-not $col) {
                    throw "列「$($p.Name)」不存在于 sheet「$Sheet」，合法列名：$($validNames -join ', ')"
                }
                Set-CellValue $ws $target $col.Col $p.Value
                $cellCount++
            }
            $rowCount++
        }
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已更新 $(Split-Path -Leaf $fullWorkbook) 的 sheet「$Sheet」$rowCount 行、共 $cellCount 个单元格。"
    }
    elseif ($Action -eq 'AddColumn') {
        if ([string]::IsNullOrWhiteSpace($Var)) { throw 'AddColumn 需要 -Var（列变量名）。' }
        if ([string]::IsNullOrWhiteSpace($Type)) { throw 'AddColumn 需要 -Type（Luban 类型）。' }

        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $header = Get-HeaderRows $ws
        $cols = Get-VarColumns $ws $header['##var'] $ext.LastCol
        foreach ($col in $cols) {
            if ($col.Name -eq $Var) { throw "列「$Var」已存在于 sheet「$Sheet」（第 $($col.Col) 列），未改动。" }
        }
        if ($header.ContainsKey('##group') -and [string]::IsNullOrWhiteSpace($Group)) {
            throw "sheet「$Sheet」有 ##group 行，加列必须传 -Group（c=仅客户端 / s=仅服务端 / 'c,s'=两端都有）。"
        }
        $dataStart = Get-DataStartRow $ws

        # 接在最后一个**有名字**的列后面，而不是 UsedRange 的末列 —— 表尾常有残留格式，
        # 按 UsedRange 会在中间留出空列，Luban 读表头会因为列名为空而报错。
        # ⚠️ 表头各行的行号**从 $header 里取**，别写死 1/2/3 —— 有 ##group 的表是 4 行表头，
        # 写死的话中文注释会落进 ##group 行（踩过）。
        $newCol = $cols[$cols.Count - 1].Col + 1
        Set-CellValue $ws $header['##var'] $newCol $Var
        if ($header.ContainsKey('##type')) { Set-CellValue $ws $header['##type'] $newCol $Type }
        if ($header.ContainsKey('##group')) { Set-CellValue $ws $header['##group'] $newCol $Group }
        if ($header.ContainsKey('##')) { Set-CellValue $ws $header['##'] $newCol $Comment }

        # 不填初值就留空。Luban 对 bool/数字列读到空会报错，所以加这类列时记得传 -Default。
        $filled = 0
        if (-not [string]::IsNullOrEmpty($Default)) {
            for ($r = $dataStart; $r -le $ext.LastRow; $r++) {
                Set-CellValue $ws $r $newCol $Default
                $filled++
            }
        }
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已在 $(Split-Path -Leaf $fullWorkbook) 的 sheet「$Sheet」第 $newCol 列追加「$Var」($Type)，回填 $filled 行初值。"
    }
    elseif ($Action -eq 'AddEnumItems') {
        if ([string]::IsNullOrWhiteSpace($EnumName)) { throw 'AddEnumItems 需要 -EnumName。' }
        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $dataStart = Get-DataStartRow $ws

        # full_name 在第2列（B）；*items 子字段（name/alias/value/comment/tags）从 ##var 第2行里找列号。
        $subCols = @{}
        for ($c = 1; $c -le $ext.LastCol; $c++) {
            $v = $ws.Cells.Item(2, $c).Value2
            if ($null -ne $v -and "$v".Trim() -ne '') { $subCols["$v".Trim()] = $c }
        }
        foreach ($need in @('name', 'value')) {
            if (-not $subCols.ContainsKey($need)) { throw "sheet「$Sheet」第2行找不到 *items 子列「$need」，无法定位枚举项列。" }
        }

        $blockStart = -1
        $blockEnd = -1
        for ($r = $dataStart; $r -le $ext.LastRow; $r++) {
            $fn = $ws.Cells.Item($r, 2).Value2
            if ($null -ne $fn -and "$fn".Trim() -ne '') {
                if ($blockStart -ge 0) { $blockEnd = $r - 1; break }
                if ("$fn".Trim() -eq $EnumName) { $blockStart = $r }
            }
        }
        if ($blockStart -lt 0) { throw "在 sheet「$Sheet」里找不到枚举「$EnumName」。" }
        if ($blockEnd -lt 0) { $blockEnd = $ext.LastRow }

        $items = Read-JsonFile $File
        $insertAt = $blockEnd + 1
        $count = 0
        foreach ($it in $items) {
            $rowsObj = $ws.Rows.Item($insertAt)
            $rowsObj.Insert(-4121)  # xlShiftDown，并自动调整其它公式里引用到被移动行的引用
            Release-Com $rowsObj
            if ($subCols.ContainsKey('name')) { Set-CellValue $ws $insertAt $subCols['name'] $it.name }
            if ($subCols.ContainsKey('alias') -and $it.PSObject.Properties['alias']) { Set-CellValue $ws $insertAt $subCols['alias'] $it.alias }
            Set-CellValue $ws $insertAt $subCols['value'] $it.value
            if ($subCols.ContainsKey('comment') -and $it.PSObject.Properties['comment']) { Set-CellValue $ws $insertAt $subCols['comment'] $it.comment }
            if ($subCols.ContainsKey('tags') -and $it.PSObject.Properties['tags']) { Set-CellValue $ws $insertAt $subCols['tags'] $it.tags }
            $insertAt++
            $count++
        }
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已向 $(Split-Path -Leaf $fullWorkbook) 的枚举「$EnumName」追加 $count 项。"
    }
    elseif ($Action -eq 'AddEnumType') {
        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $subCols = @{}
        for ($c = 1; $c -le $ext.LastCol; $c++) {
            $v = $ws.Cells.Item(2, $c).Value2
            if ($null -ne $v -and "$v".Trim() -ne '') { $subCols["$v".Trim()] = $c }
        }

        $spec = Read-JsonFile $File
        $r = $ext.LastRow + 1
        $first = $true
        $count = 0
        foreach ($it in $spec.items) {
            if ($first) {
                Set-CellValue $ws $r 2 $spec.fullName
                if ($spec.PSObject.Properties['flags']) { Set-CellValue $ws $r 3 $spec.flags }
                if ($spec.PSObject.Properties['unique']) { Set-CellValue $ws $r 4 $spec.unique }
                $first = $false
            }
            if ($subCols.ContainsKey('name')) { Set-CellValue $ws $r $subCols['name'] $it.name }
            if ($subCols.ContainsKey('alias') -and $it.PSObject.Properties['alias']) { Set-CellValue $ws $r $subCols['alias'] $it.alias }
            Set-CellValue $ws $r $subCols['value'] $it.value
            if ($subCols.ContainsKey('comment') -and $it.PSObject.Properties['comment']) { Set-CellValue $ws $r $subCols['comment'] $it.comment }
            $r++
            $count++
        }
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已在 $(Split-Path -Leaf $fullWorkbook) 追加新枚举「$($spec.fullName)」，$count 项。"
    }
    elseif ($Action -eq 'SetHeader') {
        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $header = Get-HeaderRows $ws

        # 老表可能没有 ##group 行，缺就补一行，插在 ##type 后面（没有 ##type 就插在 ##var 后面）。
        if (-not $header.ContainsKey('##group')) {
            $anchor = if ($header.ContainsKey('##type')) { $header['##type'] } else { $header['##var'] }
            $rowObj = $ws.Rows.Item($anchor + 1)
            [void]$rowObj.Insert(-4121)  # xlShiftDown
            Release-Com $rowObj
            Set-CellValue $ws ($anchor + 1) 1 '##group'
            $header = Get-HeaderRows $ws
            $ext = Get-UsedExtent $ws
            Write-Output "  （sheet「$Sheet」原来没有 ##group 行，已插在第 $($anchor + 1) 行）"
        }

        $cols = Get-VarColumns $ws $header['##var'] $ext.LastCol
        $validNames = $cols | ForEach-Object { $_.Name }

        $updates = Read-JsonFile $File
        $count = 0
        foreach ($item in $updates) {
            $name = "$($item.var)".Trim()
            $col = $cols | Where-Object { $_.Name -eq $name }
            if (-not $col) {
                throw "列「$name」不存在于 sheet「$Sheet」，合法列名：$($validNames -join ', ')"
            }
            if ($item.PSObject.Properties['group']) {
                Set-CellValue $ws $header['##group'] $col.Col $item.group
            }
            if ($item.PSObject.Properties['comment'] -and $header.ContainsKey('##')) {
                Set-CellValue $ws $header['##'] $col.Col $item.comment
            }
            $count++
        }
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已更新 $(Split-Path -Leaf $fullWorkbook) 的 sheet「$Sheet」$count 列的表头。"
    }
    elseif ($Action -eq 'DeleteRows') {
        if ([string]::IsNullOrWhiteSpace($Keys)) { throw 'DeleteRows 需要 -Keys（逗号分隔的主键值）。' }

        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $cols = Get-VarColumns $ws 1 $ext.LastCol
        $dataStart = Get-DataStartRow $ws

        $keyName = $cols[0].Name
        $keyCol = $cols[0].Col

        $rowByKey = @{}
        for ($r = $dataStart; $r -le $ext.LastRow; $r++) {
            $v = $ws.Cells.Item($r, $keyCol).Value2
            if ($null -eq $v -or "$v".Trim() -eq '') { continue }
            $rowByKey["$v".Trim()] = $r
        }

        # 先全部解析定位再删：有一个主键找不到就整份不保存，避免删掉一半留下不一致的表。
        $targets = @()
        foreach ($k in ($Keys -split ',')) {
            $key = $k.Trim()
            if ($key -eq '') { continue }
            if (-not $rowByKey.ContainsKey($key)) {
                throw "sheet「$Sheet」里找不到 $keyName = $key 的数据行，未改动。"
            }
            $targets += $rowByKey[$key]
        }

        # 从下往上删，否则删完一行后面的行号会整体上移、后续定位全错位。
        foreach ($r in ($targets | Sort-Object -Descending)) {
            $rowObj = $ws.Rows.Item($r)
            [void]$rowObj.Delete()
            Release-Com $rowObj
        }
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已从 $(Split-Path -Leaf $fullWorkbook) 的 sheet「$Sheet」删除 $($targets.Count) 行（按 $keyName）。"
    }
    elseif ($Action -eq 'DeleteColumn') {
        if ([string]::IsNullOrWhiteSpace($Var)) { throw 'DeleteColumn 需要 -Var（列变量名）。' }

        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $cols = Get-VarColumns $ws 1 $ext.LastCol
        $target = $cols | Where-Object { $_.Name -eq $Var }
        if (-not $target) {
            throw "列「$Var」不存在于 sheet「$Sheet」，合法列名：$(($cols | ForEach-Object { $_.Name }) -join ', ')"
        }
        if ($cols[0].Name -eq $Var) {
            throw "「$Var」是 sheet「$Sheet」的主键列（##var 行第一个字段），删了整张表就没法定位行了，未改动。"
        }

        $colObj = $ws.Columns.Item($target.Col)
        [void]$colObj.Delete()
        Release-Com $colObj
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已从 $(Split-Path -Leaf $fullWorkbook) 的 sheet「$Sheet」删除列「$Var」（原第 $($target.Col) 列）。"
    }
    elseif ($Action -eq 'DeleteSheet') {
        if ($wb.Worksheets.Count -le 1) {
            throw "$(Split-Path -Leaf $fullWorkbook) 只剩这一张 sheet，Excel 不允许删空工作簿。要整表作废请直接删文件。"
        }
        $exists = $false
        foreach ($s in $wb.Worksheets) { if ($s.Name -eq $Sheet) { $exists = $true }; Release-Com $s }
        if (-not $exists) { throw "sheet「$Sheet」不存在于 $(Split-Path -Leaf $fullWorkbook)，未改动。" }

        $ws = $wb.Worksheets.Item($Sheet)
        [void]$ws.Delete()
        Release-Com $ws
        $wb.Save()
        Write-Output "OK: 已从 $(Split-Path -Leaf $fullWorkbook) 删除 sheet「$Sheet」。"
    }
    else {
        # Dump
        $ws = $wb.Worksheets.Item($Sheet)
        $ext = Get-UsedExtent $ws
        $dataStart = Get-DataStartRow $ws

        if (-not [string]::IsNullOrWhiteSpace($EnumName)) {
            $subCols = @{}
            for ($c = 1; $c -le $ext.LastCol; $c++) {
                $v = $ws.Cells.Item(2, $c).Value2
                if ($null -ne $v -and "$v".Trim() -ne '') { $subCols["$v".Trim()] = $c }
            }
            $blockStart = -1
            $blockEnd = -1
            for ($r = $dataStart; $r -le $ext.LastRow; $r++) {
                $fn = $ws.Cells.Item($r, 2).Value2
                if ($null -ne $fn -and "$fn".Trim() -ne '') {
                    if ($blockStart -ge 0) { $blockEnd = $r - 1; break }
                    if ("$fn".Trim() -eq $EnumName) { $blockStart = $r }
                }
            }
            if ($blockStart -lt 0) { throw "在 sheet「$Sheet」里找不到枚举「$EnumName」。" }
            if ($blockEnd -lt 0) { $blockEnd = $ext.LastRow }
            Write-Output "ENUM $EnumName  rows $blockStart-$blockEnd"
            $order = @('name', 'alias', 'value', 'comment', 'tags') | Where-Object { $subCols.ContainsKey($_) }
            Write-Output ($order -join "`t")
            for ($r = $blockStart; $r -le $blockEnd; $r++) {
                $vals = $order | ForEach-Object { $ws.Cells.Item($r, $subCols[$_]).Value2 }
                Write-Output ($vals -join "`t")
            }
        }
        else {
            # 表头行号从标记找，不写死 —— 有 ##group 的表是 4 行表头，写死会把 group 行当成注释行打出来
            $header = Get-HeaderRows $ws
            $cols = Get-VarColumns $ws $header['##var'] $ext.LastCol
            Write-Output "SHEET $Sheet  dataRows $($ext.LastRow - $dataStart + 1)  cols $($cols.Count)"
            Write-Output ('VAR    : ' + (($cols | ForEach-Object { $_.Name }) -join "`t"))
            if ($header.ContainsKey('##type')) {
                Write-Output ('TYPE   : ' + (($cols | ForEach-Object { $ws.Cells.Item($header['##type'], $_.Col).Value2 }) -join "`t"))
            }
            if ($header.ContainsKey('##group')) {
                Write-Output ('GROUP  : ' + (($cols | ForEach-Object { $ws.Cells.Item($header['##group'], $_.Col).Value2 }) -join "`t"))
            }
            else {
                Write-Output 'GROUP  : (这张表没有 ##group 行)'
            }
            if ($header.ContainsKey('##')) {
                Write-Output ('COMMENT: ' + (($cols | ForEach-Object { $ws.Cells.Item($header['##'], $_.Col).Value2 }) -join "`t"))
            }
            $endRow = $ext.LastRow
            if ($MaxRows -gt 0 -and ($dataStart + $MaxRows - 1) -lt $endRow) { $endRow = $dataStart + $MaxRows - 1 }
            for ($r = $dataStart; $r -le $endRow; $r++) {
                $vals = $cols | ForEach-Object { $ws.Cells.Item($r, $_.Col).Value2 }
                Write-Output "${r}: $($vals -join "`t")"
            }
            if ($endRow -lt $ext.LastRow) { Write-Output "... 还有 $($ext.LastRow - $endRow) 行未显示，加大 -MaxRows 查看" }
        }
        Release-Com $ws
    }
    $succeeded = $true
}
finally {
    if ($null -ne $wb) {
        # 只在整个操作都跑完（没抛异常）才保存；中途出错的半成品改动绝不落盘，防止写坏源表。
        if ($succeeded -and $Action -ne 'Dump') { $wb.Save() }
        $wb.Close($false)
        Release-Com $wb
    }
    $excel.Quit()
    Release-Com $excel
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
}
