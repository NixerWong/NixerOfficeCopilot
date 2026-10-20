using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace OfficeCopilot.OpenXml.Word.SmokeTest.ExcelEvalFixtures
{
    internal static class ExcelEvalFixtureExporter
    {
        private const int Seed = 20261004;
        private static readonly string[] Departments = { "销售一部", "销售二部", "市场部", "华东部", "华南部" };
        private static readonly string[] Regions = { "华东", "华南", "华北", "西南" };
        private static readonly string[] Products = { "标准版", "专业版", "企业版", "服务包" };
        private static readonly string[] Channels = { "直销", "代理", "电商", "门店" };
        private static readonly string[] Salespeople = { "销售甲", "销售乙", "销售丙", "销售丁", "销售戊" };
        private static readonly string[] Months =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };
        private static readonly string[][] MonthlyUnionDepartments =
        {
            new[] { "North", "South", "North" },
            new[] { "North", "East", "South" },
            new[] { "East", "South", "North" }
        };
        private static readonly decimal[][] MonthlyUnionRevenues =
        {
            new[] { 100m, 200m, 50m },
            new[] { 80m, 120m, 50m },
            new[] { 90m, 60m, 40m }
        };

        private sealed class SalesRecord
        {
            public int Index;
            public DateTime Date;
            public string Department;
            public string Region;
            public string Product;
            public int Quantity;
            public decimal UnitPrice;
            public decimal Revenue;
        }

        public static int Export(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                Console.Error.WriteLine("An output directory is required.");
                return 2;
            }

            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            var fixtureNames = new[]
            {
                "sales_clean", "sales_large", "sales_subtotal", "sales_dirty",
                "orders_customers", "wide_monthly", "header_multirow",
                "monthly_union", "monthly_schema_mismatch", "orders_lookup"
            };
            var outputFiles = fixtureNames
                .SelectMany(name => new[] { name + ".xlsx", name + ".answers.json" })
                .Select(name => Path.Combine(outputDirectory, name))
                .ToArray();

            if (outputFiles.Any(File.Exists))
            {
                Console.Error.WriteLine("Refusing to overwrite existing Excel Eval fixture files.");
                return 2;
            }

            var stagingDirectory = Path.Combine(outputDirectory, ".excel-eval-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            var movedFiles = new List<string>();
            try
            {
                ExportSalesClean(stagingDirectory);
                ExportSalesLarge(stagingDirectory);
                ExportSalesSubtotal(stagingDirectory);
                ExportSalesDirty(stagingDirectory);
                ExportOrdersCustomers(stagingDirectory);
                ExportWideMonthly(stagingDirectory);
                ExportMultirowHeader(stagingDirectory);
                ExportMonthlyUnion(stagingDirectory);
                ExportMonthlySchemaMismatch(stagingDirectory);
                ExportOrdersLookup(stagingDirectory);

                if (outputFiles.Any(File.Exists))
                    throw new IOException("An Excel Eval fixture appeared in the output directory during generation.");

                foreach (var stagedFile in Directory.GetFiles(stagingDirectory))
                {
                    var destination = Path.Combine(outputDirectory, Path.GetFileName(stagedFile));
                    File.Move(stagedFile, destination);
                    movedFiles.Add(destination);
                }

                Directory.Delete(stagingDirectory);
                Console.WriteLine("Excel Eval fixtures written to: " + outputDirectory);
                return 0;
            }
            catch (Exception ex)
            {
                foreach (var movedFile in movedFiles)
                {
                    if (File.Exists(movedFile))
                        File.Delete(movedFile);
                }

                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, true);

                Console.Error.WriteLine("Excel Eval fixture export failed: " + ex.Message);
                return 1;
            }
        }

        private static void ExportSalesClean(string directory)
        {
            const int count = 10000;
            var workbookPath = Path.Combine(directory, "sales_clean.xlsx");
            var records = Enumerable.Range(0, count).Select(index => CreateSalesRecord(index));
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Sales", SalesRows(records, includeExtendedColumns: false), "A1:H10001")
            });

            var yearRecords = Enumerable.Range(0, count)
                .Select(index => CreateSalesRecord(index))
                .Where(record => record.Date.Year == 2025)
                .ToList();
            var total = yearRecords.Sum(record => record.Revenue);
            var byDepartment = yearRecords
                .GroupBy(record => record.Department)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new Dictionary<string, object>
                {
                    ["department"] = group.Key,
                    ["revenue"] = group.Sum(record => record.Revenue),
                    ["sharePercent"] = total == 0 ? 0m : decimal.Round(group.Sum(record => record.Revenue) / total * 100m, 4)
                })
                .ToList();

            WriteAnswers(directory, "sales_clean", 10001, new Dictionary<string, object>
            {
                ["totalRevenue2025"] = total,
                ["dept_2025"] = byDepartment
            });
        }

        private static void ExportSalesLarge(string directory)
        {
            const int count = 200000;
            var workbookPath = Path.Combine(directory, "sales_large.xlsx");
            var records = Enumerable.Range(0, count).Select(index => CreateSalesRecord(index));
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Sales", SalesRows(records, includeExtendedColumns: true), "A1:L200001")
            });

            WriteAnswers(directory, "sales_large", count + 1, new Dictionary<string, object>
            {
                ["detailRowCount"] = count,
                ["totalRevenue"] = Enumerable.Range(0, count).Sum(index => CreateSalesRecord(index).Revenue)
            });
        }

        private static void ExportSalesSubtotal(string directory)
        {
            const int recordsPerDepartment = 1000;
            var workbookPath = Path.Combine(directory, "sales_subtotal.xlsx");
            var rowCount = 1 + recordsPerDepartment * Departments.Length + Departments.Length + 1;
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Sales", SubtotalSalesRows(recordsPerDepartment), "A1:H" + rowCount.ToString(CultureInfo.InvariantCulture))
            });

            var records = Enumerable.Range(0, recordsPerDepartment * Departments.Length)
                .Select(index => CreateSalesRecord(index, Departments[index / recordsPerDepartment]))
                .ToList();
            WriteAnswers(directory, "sales_subtotal", rowCount, new Dictionary<string, object>
            {
                ["detailRowCount"] = records.Count,
                ["detailRevenueTotal"] = records.Sum(record => record.Revenue),
                ["dept_total_excluding_subtotals"] = records
                    .GroupBy(record => record.Department)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new Dictionary<string, object>
                    {
                        ["department"] = group.Key,
                        ["revenue"] = group.Sum(record => record.Revenue)
                    })
                    .ToList()
            });
        }

        private static void ExportSalesDirty(string directory)
        {
            const int count = 5000;
            var workbookPath = Path.Combine(directory, "sales_dirty.xlsx");
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Sales", DirtySalesRows(count), "A1:H5001")
            });

            var records = Enumerable.Range(0, count).Select(index => CreateSalesRecord(index)).ToList();
            var numericRecords = records.Where(record => IndexCategory(record.Index) == 3).ToList();
            var textNumberRecords = records.Where(record => IndexCategory(record.Index) == 2).ToList();
            var errorCount = records.Count(record => IndexCategory(record.Index) == 1);
            var blankCount = records.Count(record => IndexCategory(record.Index) == 0);
            WriteAnswers(directory, "sales_dirty", count + 1, new Dictionary<string, object>
            {
                ["numericRevenueSum"] = numericRecords.Sum(record => record.Revenue),
                ["textNumberRevenueSum"] = textNumberRecords.Sum(record => record.Revenue),
                ["numericAndTextRevenueSum"] = numericRecords.Sum(record => record.Revenue) + textNumberRecords.Sum(record => record.Revenue),
                ["numericCount"] = numericRecords.Count,
                ["textNumberCount"] = textNumberRecords.Count,
                ["errorCount"] = errorCount,
                ["blankCount"] = blankCount,
                ["hiddenRows"] = new[] { 11, 12 }
            });
        }

        private static void ExportOrdersCustomers(string directory)
        {
            const int count = 3000;
            var workbookPath = Path.Combine(directory, "orders_customers.xlsx");
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Orders", OrderRows(count), "A1:C3001"),
                new FixtureSheet("Customers", CustomerRows(count), "A1:C3001")
            });

            var orderKeys = Enumerable.Range(1, count).Select(index => CustomerKey(index)).ToHashSet(StringComparer.Ordinal);
            var customerKeys = Enumerable.Range(1, count - 150)
                .Select(index => CustomerKey(index))
                .Concat(Enumerable.Range(count + 1, 150).Select(CustomerKey))
                .ToHashSet(StringComparer.Ordinal);
            WriteAnswers(directory, "orders_customers", count * 2 + 2, new Dictionary<string, object>
            {
                ["orderCount"] = count,
                ["customerCount"] = count,
                ["missingCustomerKeys"] = orderKeys.Except(customerKeys).OrderBy(key => key, StringComparer.Ordinal).ToList(),
                ["extraCustomerKeys"] = customerKeys.Except(orderKeys).OrderBy(key => key, StringComparer.Ordinal).ToList()
            });
        }

        private static void ExportWideMonthly(string directory)
        {
            const int count = 500;
            var workbookPath = Path.Combine(directory, "wide_monthly.xlsx");
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Monthly", WideMonthlyRows(count), "A1:N501")
            });

            var monthlyTotals = Enumerable.Range(0, Months.Length)
                .Select(monthIndex => new Dictionary<string, object>
                {
                    ["month"] = Months[monthIndex],
                    ["revenue"] = Enumerable.Range(0, count).Sum(index => MonthlyValue(index, monthIndex))
                })
                .ToList();
            WriteAnswers(directory, "wide_monthly", count + 1, new Dictionary<string, object>
            {
                ["monthlyTotals"] = monthlyTotals,
                ["grandTotal"] = monthlyTotals.Sum(item => (decimal)item["revenue"])
            });
        }

        private static void ExportMultirowHeader(string directory)
        {
            const int count = 1000;
            var workbookPath = Path.Combine(directory, "header_multirow.xlsx");
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Orders", MultirowHeaderRows(count), "A2:D1002", new[] { "A1:B1", "C1:D1" })
            });

            var total = Enumerable.Range(0, count).Sum(index => 100m + ((index + Seed) * 113L % 50000L) / 100m);
            WriteAnswers(directory, "header_multirow", count + 2, new Dictionary<string, object>
            {
                ["detailRowCount"] = count,
                ["amountTotal"] = total,
                ["quantityTotal"] = Enumerable.Range(0, count).Sum(index => 1 + (index * 7) % 20)
            });
        }

        private static void ExportMonthlyUnion(string directory)
        {
            var workbookPath = Path.Combine(directory, "monthly_union.xlsx");
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Jan", MonthlyUnionRows(0), "A1:B4"),
                new FixtureSheet("Feb", MonthlyUnionRows(1), "A1:B4"),
                new FixtureSheet("Mar", MonthlyUnionRows(2), "A1:B4")
            });

            var allValues = MonthlyUnionRevenues.SelectMany(month => month).ToList();
            WriteAnswers(directory, "monthly_union", 12, new Dictionary<string, object>
            {
                ["revenueTotal"] = allValues.Sum(),
                ["averageRevenue"] = allValues.Average(),
                ["detailRowCount"] = allValues.Count
            });
        }

        private static void ExportMonthlySchemaMismatch(string directory)
        {
            var workbookPath = Path.Combine(directory, "monthly_schema_mismatch.xlsx");
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Jan", MonthlySchemaRows(0), "A1:B3"),
                new FixtureSheet("Feb", MonthlySchemaRows(1), "A1:B3"),
                new FixtureSheet("Mar", MonthlySchemaRows(2), "A1:C3")
            });

            WriteAnswers(directory, "monthly_schema_mismatch", 9, new Dictionary<string, object>
            {
                ["inconsistentHeaders"] = true,
                ["orderMismatchWorksheet"] = "Feb",
                ["extraColumnWorksheet"] = "Mar",
                ["extraColumn"] = "Channel"
            });
        }

        private static void ExportOrdersLookup(string directory)
        {
            var workbookPath = Path.Combine(directory, "orders_lookup.xlsx");
            FixtureWorkbookWriter.Write(workbookPath, new[]
            {
                new FixtureSheet("Orders", LookupOrderRows(), "A1:C4"),
                new FixtureSheet("Customers", LookupCustomerRows(includeDuplicate: false), "A1:B3"),
                new FixtureSheet("CustomersDuplicate", LookupCustomerRows(includeDuplicate: true), "A1:B4")
            });

            WriteAnswers(directory, "orders_lookup", 11, new Dictionary<string, object>
            {
                ["totalAmount"] = 350m,
                ["unmatchedRowCount"] = 1,
                ["duplicateKey"] = "C1"
            });
        }

        private static IEnumerable<FixtureRow> MonthlyUnionRows(int monthIndex)
        {
            yield return TextRow(1, "Department", "Revenue");
            for (var index = 0; index < MonthlyUnionRevenues[monthIndex].Length; index++)
            {
                yield return new FixtureRow(index + 2, new[]
                {
                    FixtureCell.TextValue(MonthlyUnionDepartments[monthIndex][index]),
                    FixtureCell.NumberValue(MonthlyUnionRevenues[monthIndex][index])
                });
            }
        }

        private static IEnumerable<FixtureRow> MonthlySchemaRows(int monthIndex)
        {
            if (monthIndex == 1)
            {
                yield return TextRow(1, "Revenue", "Region");
                yield return new FixtureRow(2, new[]
                {
                    FixtureCell.NumberValue(75m),
                    FixtureCell.TextValue("Central")
                });
                yield return new FixtureRow(3, new[]
                {
                    FixtureCell.NumberValue(90m),
                    FixtureCell.TextValue("East")
                });
                yield break;
            }

            if (monthIndex == 2)
            {
                yield return TextRow(1, "Region", "Revenue", "Channel");
                yield return new FixtureRow(2, new[]
                {
                    FixtureCell.TextValue("East"),
                    FixtureCell.NumberValue(100m),
                    FixtureCell.TextValue("Direct")
                });
                yield return new FixtureRow(3, new[]
                {
                    FixtureCell.TextValue("South"),
                    FixtureCell.NumberValue(125m),
                    FixtureCell.TextValue("Retail")
                });
                yield break;
            }

            yield return TextRow(1, "Region", "Revenue");
            yield return new FixtureRow(2, new[]
            {
                FixtureCell.TextValue("East"),
                FixtureCell.NumberValue(100m)
            });
            yield return new FixtureRow(3, new[]
            {
                FixtureCell.TextValue("West"),
                FixtureCell.NumberValue(50m)
            });
        }

        private static IEnumerable<FixtureRow> LookupOrderRows()
        {
            yield return TextRow(1, "OrderID", "CustomerID", "Amount");
            yield return new FixtureRow(2, new[]
            {
                FixtureCell.TextValue("O1"),
                FixtureCell.TextValue("C1"),
                FixtureCell.NumberValue(100m)
            });
            yield return new FixtureRow(3, new[]
            {
                FixtureCell.TextValue("O2"),
                FixtureCell.TextValue("C2"),
                FixtureCell.NumberValue(200m)
            });
            yield return new FixtureRow(4, new[]
            {
                FixtureCell.TextValue("O3"),
                FixtureCell.TextValue("C9"),
                FixtureCell.NumberValue(50m)
            });
        }

        private static IEnumerable<FixtureRow> LookupCustomerRows(bool includeDuplicate)
        {
            yield return TextRow(1, "CustomerID", "Name");
            yield return new FixtureRow(2, new[]
            {
                FixtureCell.TextValue("C1"),
                FixtureCell.TextValue("Alice")
            });
            if (includeDuplicate)
            {
                yield return new FixtureRow(3, new[]
                {
                    FixtureCell.TextValue("C1"),
                    FixtureCell.TextValue("Duplicate Alice")
                });
                yield return new FixtureRow(4, new[]
                {
                    FixtureCell.TextValue("C2"),
                    FixtureCell.TextValue("Bob")
                });
                yield break;
            }

            yield return new FixtureRow(3, new[]
            {
                FixtureCell.TextValue("C2"),
                FixtureCell.TextValue("Bob")
            });
        }

        private static IEnumerable<FixtureRow> SalesRows(IEnumerable<SalesRecord> records, bool includeExtendedColumns)
        {
            var headers = includeExtendedColumns
                ? new[] { "OrderID", "OrderDate", "Department", "Region", "Product", "Quantity", "UnitPrice", "Revenue", "Channel", "Salesperson", "Quarter", "Year" }
                : new[] { "OrderID", "OrderDate", "Department", "Region", "Product", "Quantity", "UnitPrice", "Revenue" };
            yield return TextRow(1, headers);
            var rowIndex = 2;
            foreach (var record in records)
            {
                var cells = SalesCells(record);
                if (includeExtendedColumns)
                {
                    cells.Add(FixtureCell.TextValue(Channels[record.Index % Channels.Length]));
                    cells.Add(FixtureCell.TextValue(Salespeople[record.Index % Salespeople.Length]));
                    cells.Add(FixtureCell.TextValue("Q" + (((record.Date.Month - 1) / 3) + 1).ToString(CultureInfo.InvariantCulture)));
                    cells.Add(FixtureCell.NumberValue(record.Date.Year));
                }

                yield return new FixtureRow(rowIndex++, cells);
            }
        }

        private static IEnumerable<FixtureRow> SubtotalSalesRows(int recordsPerDepartment)
        {
            yield return TextRow(1, "OrderID", "OrderDate", "Department", "Region", "Product", "Quantity", "UnitPrice", "Revenue");
            var rowIndex = 2;
            for (var departmentIndex = 0; departmentIndex < Departments.Length; departmentIndex++)
            {
                var department = Departments[departmentIndex];
                var firstDetailRow = rowIndex;
                var records = Enumerable.Range(0, recordsPerDepartment)
                    .Select(offset => CreateSalesRecord(departmentIndex * recordsPerDepartment + offset, department))
                    .ToList();
                foreach (var record in records)
                    yield return new FixtureRow(rowIndex++, SalesCells(record));

                var lastDetailRow = rowIndex - 1;
                var subtotal = records.Sum(record => record.Revenue);
                var subtotalCells = new FixtureCell[8];
                subtotalCells[2] = FixtureCell.TextValue(department + " 小计");
                subtotalCells[7] = FixtureCell.FormulaValue(
                    "SUBTOTAL(9,H" + firstDetailRow.ToString(CultureInfo.InvariantCulture) + ":H" + lastDetailRow.ToString(CultureInfo.InvariantCulture) + ")",
                    subtotal);
                yield return new FixtureRow(rowIndex++, subtotalCells);
            }

            var total = Enumerable.Range(0, recordsPerDepartment * Departments.Length)
                .Select(index => CreateSalesRecord(index, Departments[index / recordsPerDepartment]))
                .Sum(record => record.Revenue);
            var totalCells = new FixtureCell[8];
            totalCells[2] = FixtureCell.TextValue("合计");
            totalCells[7] = FixtureCell.NumberValue(total);
            yield return new FixtureRow(rowIndex, totalCells);
        }

        private static IEnumerable<FixtureRow> DirtySalesRows(int count)
        {
            yield return TextRow(1, "OrderID", "OrderDate", "Department", "Region", "Product", "Quantity", "UnitPrice", "Revenue");
            for (var index = 0; index < count; index++)
            {
                var record = CreateSalesRecord(index);
                var cells = SalesCells(record);
                switch (IndexCategory(index))
                {
                    case 0:
                        cells[7] = FixtureCell.BlankValue();
                        break;
                    case 1:
                        cells[7] = FixtureCell.ErrorValue("#N/A");
                        break;
                    case 2:
                        cells[7] = FixtureCell.TextValue(record.Revenue.ToString(CultureInfo.InvariantCulture));
                        break;
                }

                yield return new FixtureRow(index + 2, cells, index == 9 || index == 10);
            }
        }

        private static IEnumerable<FixtureRow> OrderRows(int count)
        {
            yield return TextRow(1, "OrderID", "CustomerID", "Amount");
            for (var index = 1; index <= count; index++)
            {
                yield return new FixtureRow(index + 1, new[]
                {
                    FixtureCell.TextValue("O" + index.ToString("D6", CultureInfo.InvariantCulture)),
                    FixtureCell.TextValue(CustomerKey(index)),
                    FixtureCell.NumberValue(25m + ((index + Seed) * 19L % 25000L) / 100m)
                });
            }
        }

        private static IEnumerable<FixtureRow> CustomerRows(int count)
        {
            yield return TextRow(1, "CustomerID", "Name", "Segment");
            var rowIndex = 2;
            foreach (var index in Enumerable.Range(1, count - 150).Concat(Enumerable.Range(count + 1, 150)))
            {
                yield return new FixtureRow(rowIndex++, new[]
                {
                    FixtureCell.TextValue(CustomerKey(index)),
                    FixtureCell.TextValue("客户" + index.ToString("D4", CultureInfo.InvariantCulture)),
                    FixtureCell.TextValue(new[] { "零售", "企业", "渠道" }[index % 3])
                });
            }
        }

        private static IEnumerable<FixtureRow> WideMonthlyRows(int count)
        {
            yield return TextRow(1, new[] { "Store", "Department" }.Concat(Months).ToArray());
            for (var index = 0; index < count; index++)
            {
                var cells = new List<FixtureCell>
                {
                    FixtureCell.TextValue("S" + (index + 1).ToString("D4", CultureInfo.InvariantCulture)),
                    FixtureCell.TextValue(Departments[index % Departments.Length])
                };
                for (var month = 0; month < Months.Length; month++)
                    cells.Add(FixtureCell.NumberValue(MonthlyValue(index, month)));
                yield return new FixtureRow(index + 2, cells);
            }
        }

        private static IEnumerable<FixtureRow> MultirowHeaderRows(int count)
        {
            yield return new FixtureRow(1, new[]
            {
                FixtureCell.TextValue("订单信息"),
                FixtureCell.BlankValue(),
                FixtureCell.TextValue("销售指标"),
                FixtureCell.BlankValue()
            });
            yield return TextRow(2, "OrderID", "OrderDate", "Amount", "Quantity");
            for (var index = 0; index < count; index++)
            {
                yield return new FixtureRow(index + 3, new[]
                {
                    FixtureCell.TextValue("M" + (index + 1).ToString("D5", CultureInfo.InvariantCulture)),
                    FixtureCell.DateValue(new DateTime(2025, 1, 1).AddDays((index * 11) % 365)),
                    FixtureCell.NumberValue(100m + ((index + Seed) * 113L % 50000L) / 100m),
                    FixtureCell.NumberValue(1 + (index * 7) % 20)
                });
            }
        }

        private static List<FixtureCell> SalesCells(SalesRecord record)
        {
            return new List<FixtureCell>
            {
                FixtureCell.TextValue("S" + (record.Index + 1).ToString("D7", CultureInfo.InvariantCulture)),
                FixtureCell.DateValue(record.Date),
                FixtureCell.TextValue(record.Department),
                FixtureCell.TextValue(record.Region),
                FixtureCell.TextValue(record.Product),
                FixtureCell.NumberValue(record.Quantity),
                FixtureCell.NumberValue(record.UnitPrice),
                FixtureCell.NumberValue(record.Revenue)
            };
        }

        private static FixtureRow TextRow(int row, params string[] values)
        {
            return new FixtureRow(row, values.Select(FixtureCell.TextValue).ToArray());
        }

        private static SalesRecord CreateSalesRecord(int index, string departmentOverride = null)
        {
            var seededIndex = index + Seed;
            var dateYear = 2024 + (seededIndex % 2);
            var date = new DateTime(dateYear, 1, 1).AddDays((seededIndex * 17L) % 365L);
            var quantity = 1 + (seededIndex * 7) % 20;
            var revenue = 125m + (seededIndex * 7919L % 35000L) / 100m;
            return new SalesRecord
            {
                Index = index,
                Date = date,
                Department = departmentOverride ?? Departments[(seededIndex * 7) % Departments.Length],
                Region = Regions[(seededIndex * 11) % Regions.Length],
                Product = Products[(seededIndex * 13) % Products.Length],
                Quantity = quantity,
                UnitPrice = decimal.Round(revenue / quantity, 2),
                Revenue = revenue
            };
        }

        private static int IndexCategory(int index)
        {
            var bucket = index % 100;
            if (bucket < 5)
                return 0;
            if (bucket < 6)
                return 1;
            if (bucket < 9)
                return 2;
            return 3;
        }

        private static string CustomerKey(int index)
        {
            return "C" + index.ToString("D4", CultureInfo.InvariantCulture);
        }

        private static decimal MonthlyValue(int storeIndex, int monthIndex)
        {
            return 50m + ((storeIndex * 31L + monthIndex * 997L + Seed) % 100000L) / 100m;
        }

        private static void WriteAnswers(string directory, string fixtureName, int worksheetRowCount, IDictionary<string, object> answers)
        {
            var document = new Dictionary<string, object>
            {
                ["fixture"] = fixtureName,
                ["seed"] = Seed,
                ["worksheetRowCount"] = worksheetRowCount,
                ["answers"] = answers
            };
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var json = serializer.Serialize(document);
            File.WriteAllText(
                Path.Combine(directory, fixtureName + ".answers.json"),
                json + Environment.NewLine,
                new UTF8Encoding(false));
        }
    }
}
