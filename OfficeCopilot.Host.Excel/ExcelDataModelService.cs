using System;
using System.Collections.Generic;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel
{
    internal sealed class ExcelDataModelService : ExcelServiceBase
    {
        public ExcelDataModelService(ExcelInterop.Application application)
            : base(application)
        {
        }

        public ExcelDataModelTableListResult ListDataModelTables()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelDataModelTableListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            object model = null;
            object tables = null;
            object table = null;

            try
            {
                model = ((dynamic)workbook).Model;
                if (model == null)
                {
                    return result;
                }

                tables = ((dynamic)model).ModelTables;
                var count = Convert.ToInt32(((dynamic)tables).Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        table = ((dynamic)tables).Item(index);
                        result.Tables.Add(new ExcelDataModelTableInfo
                        {
                            Name = Convert.ToString(((dynamic)table).Name) ?? string.Empty,
                            SourceName = Convert.ToString(((dynamic)table).SourceName) ?? string.Empty,
                            RecordCount = SafeGetInt32(() => Convert.ToInt32(((dynamic)table).RecordCount))
                        });
                    }
                    finally
                    {
                        ReleaseComObject(table);
                        table = null;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(table);
                ReleaseComObject(tables);
                ReleaseComObject(model);
            }

            return result;
        }

        public ExcelDataModelMeasureListResult ListDataModelMeasures()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelDataModelMeasureListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            object model = null;
            object measures = null;
            object measure = null;

            try
            {
                model = ((dynamic)workbook).Model;
                if (model == null)
                {
                    return result;
                }

                measures = ((dynamic)model).ModelMeasures;
                var count = Convert.ToInt32(((dynamic)measures).Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        measure = ((dynamic)measures).Item(index);
                        var formula = Convert.ToString(((dynamic)measure).Formula) ?? string.Empty;

                        result.Measures.Add(new ExcelDataModelMeasureInfo
                        {
                            Name = Convert.ToString(((dynamic)measure).Name) ?? string.Empty,
                            TableName = GetAssociatedModelTableName(measure),
                            FormulaPreview = BuildFormulaPreview(formula),
                            Description = SafeGetStringDynamic(measure, "Description")
                        });
                    }
                    finally
                    {
                        ReleaseComObject(measure);
                        measure = null;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(measure);
                ReleaseComObject(measures);
                ReleaseComObject(model);
            }

            return result;
        }

        public ExcelDataModelMeasureViewResult ViewDataModelMeasure(ExcelDataModelMeasureViewRequest request)
        {
            request = request ?? new ExcelDataModelMeasureViewRequest();
            if (string.IsNullOrWhiteSpace(request.MeasureName))
            {
                throw new InvalidOperationException("Measure 名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            object model = null;
            object measures = null;
            object measure = null;

            try
            {
                model = ((dynamic)workbook).Model;
                if (model == null)
                {
                    throw new InvalidOperationException("当前工作簿没有可用的数据模型。");
                }

                measures = ((dynamic)model).ModelMeasures;
                var count = Convert.ToInt32(((dynamic)measures).Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        measure = ((dynamic)measures).Item(index);
                        var name = Convert.ToString(((dynamic)measure).Name) ?? string.Empty;
                        var tableName = GetAssociatedModelTableName(measure);

                        if (!string.Equals(name, request.MeasureName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(request.TableName)
                            && !string.Equals(tableName, request.TableName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var formula = Convert.ToString(((dynamic)measure).Formula) ?? string.Empty;

                        return new ExcelDataModelMeasureViewResult
                        {
                            WorkbookName = workbook.Name ?? string.Empty,
                            MeasureName = name,
                            TableName = tableName,
                            DaxFormula = formula,
                            Description = SafeGetStringDynamic(measure, "Description"),
                            CharacterCount = formula.Length
                        };
                    }
                    finally
                    {
                        ReleaseComObject(measure);
                        measure = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(measure);
                ReleaseComObject(measures);
                ReleaseComObject(model);
            }

            throw new InvalidOperationException("未找到 Data Model Measure：" + request.MeasureName);
        }

        public ExcelDataModelRelationshipListResult ListDataModelRelationships()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelDataModelRelationshipListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            object model = null;
            object relationships = null;
            object relationship = null;
            object foreignKeyTable = null;
            object foreignKeyColumn = null;
            object primaryKeyTable = null;
            object primaryKeyColumn = null;

            try
            {
                model = ((dynamic)workbook).Model;
                if (model == null)
                {
                    return result;
                }

                relationships = ((dynamic)model).ModelRelationships;
                var count = Convert.ToInt32(((dynamic)relationships).Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        relationship = ((dynamic)relationships).Item(index);
                        foreignKeyTable = ((dynamic)relationship).ForeignKeyTable;
                        foreignKeyColumn = ((dynamic)relationship).ForeignKeyColumn;
                        primaryKeyTable = ((dynamic)relationship).PrimaryKeyTable;
                        primaryKeyColumn = ((dynamic)relationship).PrimaryKeyColumn;

                        result.Relationships.Add(new ExcelDataModelRelationshipInfo
                        {
                            FromTable = Convert.ToString(((dynamic)foreignKeyTable).Name) ?? string.Empty,
                            FromColumn = Convert.ToString(((dynamic)foreignKeyColumn).Name) ?? string.Empty,
                            ToTable = Convert.ToString(((dynamic)primaryKeyTable).Name) ?? string.Empty,
                            ToColumn = Convert.ToString(((dynamic)primaryKeyColumn).Name) ?? string.Empty,
                            IsActive = SafeGetBoolDynamic(relationship, "Active")
                        });
                    }
                    finally
                    {
                        ReleaseComObject(primaryKeyColumn);
                        primaryKeyColumn = null;
                        ReleaseComObject(primaryKeyTable);
                        primaryKeyTable = null;
                        ReleaseComObject(foreignKeyColumn);
                        foreignKeyColumn = null;
                        ReleaseComObject(foreignKeyTable);
                        foreignKeyTable = null;
                        ReleaseComObject(relationship);
                        relationship = null;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(primaryKeyColumn);
                ReleaseComObject(primaryKeyTable);
                ReleaseComObject(foreignKeyColumn);
                ReleaseComObject(foreignKeyTable);
                ReleaseComObject(relationship);
                ReleaseComObject(relationships);
                ReleaseComObject(model);
            }

            return result;
        }

        public ExcelDataModelCalculatedColumnListResult ListDataModelCalculatedColumns()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelDataModelCalculatedColumnListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            object model = null;
            object tables = null;
            object table = null;
            object columns = null;
            object column = null;

            try
            {
                model = ((dynamic)workbook).Model;
                if (model == null)
                {
                    return result;
                }

                tables = ((dynamic)model).ModelTables;
                var tableCount = Convert.ToInt32(((dynamic)tables).Count);

                for (var tableIndex = 1; tableIndex <= tableCount; tableIndex++)
                {
                    try
                    {
                        table = ((dynamic)tables).Item(tableIndex);
                        var tableName = Convert.ToString(((dynamic)table).Name) ?? string.Empty;

                        columns = ((dynamic)table).ModelTableColumns;
                        var columnCount = Convert.ToInt32(((dynamic)columns).Count);

                        for (var columnIndex = 1; columnIndex <= columnCount; columnIndex++)
                        {
                            try
                            {
                                column = ((dynamic)columns).Item(columnIndex);
                                var formula = SafeGetStringDynamic(column, "Formula");
                                if (string.IsNullOrWhiteSpace(formula))
                                {
                                    continue;
                                }

                                result.CalculatedColumns.Add(new ExcelDataModelCalculatedColumnInfo
                                {
                                    Name = Convert.ToString(((dynamic)column).Name) ?? string.Empty,
                                    TableName = tableName,
                                    FormulaPreview = BuildFormulaPreview(formula),
                                    DataType = SafeGetStringDynamic(column, "DataType"),
                                    Description = SafeGetStringDynamic(column, "Description")
                                });
                            }
                            finally
                            {
                                ReleaseComObject(column);
                                column = null;
                            }
                        }
                    }
                    finally
                    {
                        ReleaseComObject(columns);
                        columns = null;
                        ReleaseComObject(table);
                        table = null;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(column);
                ReleaseComObject(columns);
                ReleaseComObject(table);
                ReleaseComObject(tables);
                ReleaseComObject(model);
            }

            return result;
        }

        public ExcelDataModelCalculatedColumnViewResult ViewDataModelCalculatedColumn(ExcelDataModelCalculatedColumnViewRequest request)
        {
            request = request ?? new ExcelDataModelCalculatedColumnViewRequest();
            if (string.IsNullOrWhiteSpace(request.TableName))
            {
                throw new InvalidOperationException("表名称不能为空。");
            }

            if (string.IsNullOrWhiteSpace(request.ColumnName))
            {
                throw new InvalidOperationException("列名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            object model = null;
            object tables = null;
            object table = null;
            object columns = null;
            object column = null;

            try
            {
                model = ((dynamic)workbook).Model;
                if (model == null)
                {
                    throw new InvalidOperationException("当前工作簿没有可用的数据模型。");
                }

                tables = ((dynamic)model).ModelTables;
                var tableCount = Convert.ToInt32(((dynamic)tables).Count);

                for (var tableIndex = 1; tableIndex <= tableCount; tableIndex++)
                {
                    try
                    {
                        table = ((dynamic)tables).Item(tableIndex);
                        var tableName = Convert.ToString(((dynamic)table).Name) ?? string.Empty;
                        if (!string.Equals(tableName, request.TableName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        columns = ((dynamic)table).ModelTableColumns;
                        var columnCount = Convert.ToInt32(((dynamic)columns).Count);

                        for (var columnIndex = 1; columnIndex <= columnCount; columnIndex++)
                        {
                            try
                            {
                                column = ((dynamic)columns).Item(columnIndex);
                                var columnName = Convert.ToString(((dynamic)column).Name) ?? string.Empty;
                                if (!string.Equals(columnName, request.ColumnName.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                var formula = SafeGetStringDynamic(column, "Formula");
                                if (string.IsNullOrWhiteSpace(formula))
                                {
                                    throw new InvalidOperationException("该列不是计算列：" + request.ColumnName);
                                }

                                return new ExcelDataModelCalculatedColumnViewResult
                                {
                                    WorkbookName = workbook.Name ?? string.Empty,
                                    TableName = tableName,
                                    ColumnName = columnName,
                                    DaxFormula = formula,
                                    DataType = SafeGetStringDynamic(column, "DataType"),
                                    Description = SafeGetStringDynamic(column, "Description"),
                                    CharacterCount = formula.Length
                                };
                            }
                            finally
                            {
                                ReleaseComObject(column);
                                column = null;
                            }
                        }
                    }
                    finally
                    {
                        ReleaseComObject(columns);
                        columns = null;
                        ReleaseComObject(table);
                        table = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(column);
                ReleaseComObject(columns);
                ReleaseComObject(table);
                ReleaseComObject(tables);
                ReleaseComObject(model);
            }

            throw new InvalidOperationException("未找到 Data Model 计算列：" + request.TableName + "." + request.ColumnName);
        }

        private static string GetAssociatedModelTableName(object measure)
        {
            object table = null;

            try
            {
                table = ((dynamic)measure).AssociatedTable;
                return table == null ? string.Empty : (Convert.ToString(((dynamic)table).Name) ?? string.Empty);
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                ReleaseComObject(table);
            }
        }

        private static string SafeGetStringDynamic(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return string.Empty;
            }

            try
            {
                switch (memberName)
                {
                    case "Description":
                        return Convert.ToString(((dynamic)instance).Description) ?? string.Empty;
                    case "Formula":
                        return Convert.ToString(((dynamic)instance).Formula) ?? string.Empty;
                    case "DataType":
                        return Convert.ToString(((dynamic)instance).DataType) ?? string.Empty;
                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool SafeGetBoolDynamic(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            try
            {
                switch (memberName)
                {
                    case "Active":
                        return Convert.ToBoolean(((dynamic)instance).Active);
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
