/* eslint-disable react-hooks/set-state-in-effect */
import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  AlertCircle, ArrowLeft, CheckCircle2, ClipboardList, Loader2,
  Plus, Send, ToggleLeft, ToggleRight, Trash2, Upload, X
} from "lucide-react";
import { api } from "../../api";
import ContributorLayout from "../../components/contributor/ContributorLayout";
import ProcessTracker from "../../components/contributor/ProcessTracker";
import "./DailyReport.css";

const EMPTY_FORM = {
  monthPlan: "", monthActual: "",
  productionPlan: "", productionActual: "",
  uphTarget: "", uphActual: "",
  upphInstalled: "", upphActual: "",
  cphTarget: "", cphActual: "",
  fpyTarget: "", fpyActual: "",
  ftyTarget: "", ftyActual: "",
  rtyTarget: "", rtyActual: "",
  osdReportingDateValue: "", osdReportingDatePercent: null,
  osdMtdValue: "", osdMtdPercent: null,
  plannedOTManhours: null, unplannedOTManhours: null, actualOTManhours: "",
  openWOQty: "", over7DaysWOBalanceQty: "",
  dailyTRCInQty: "", dailyTRCOutQty: "",
  trcOverallFailureInflowPercent: "", trcLyingOver3DaysCr: "",
  issueDescription: "",
};

const GROUPS = [
  {
    title: "Production",
    rows: [
      ["Month Plan", "monthPlan", "Plan"],
      ["Month Plan", "monthActual", "Actual"],
      ["Production", "productionPlan", "Plan"],
      ["Production", "productionActual", "Actual"],
      ["UPH", "uphTarget", "Target"],
      ["UPH", "uphActual", "Actual"],
      ["UPPH", "upphInstalled", "Installed"],
      ["UPPH", "upphActual", "Actual"],
      ["CPH", "cphTarget", "Target"],
      ["CPH", "cphActual", "Actual"],
    ],
  },
  {
    title: "Quality",
    rows: [
      ["FPY", "fpyTarget", "Target"], ["FPY", "fpyActual", "Actual"],
      ["FTY", "ftyTarget", "Target"], ["FTY", "ftyActual", "Actual"],
      ["RTY", "rtyTarget", "Target"], ["RTY", "rtyActual", "Actual"],
    ],
  },
  {
    title: "Operations",
    rows: [
      ["OS&D — Reporting Date", "osdReportingDateValue", "Reporting Date"],
      ["OS&D — Reporting Date %", "osdReportingDatePercent", "%"],
      ["OS&D — MTD", "osdMtdValue", "MTD"],
      ["OS&D — MTD %", "osdMtdPercent", "%"],
      ["Planned OT (Hrs)", "plannedOTManhours", "Planned"],
      ["Unplanned OT (Hrs)", "unplannedOTManhours", "Unplanned"],
      ["Actual OT (Hrs)", "actualOTManhours", "Actual"],
      ["Open WO (Qty)", "openWOQty", "Value"],
      [">7 Days WO (Qty)", "over7DaysWOBalanceQty", "Value"],
    ],
  },
  {
    title: "TRC",
    rows: [
      ["Daily TRC In (Qty)", "dailyTRCInQty", "Value"],
      ["Daily TRC Out (Qty)", "dailyTRCOutQty", "Value"],
      ["TRC Overall Failure Inflow %", "trcOverallFailureInflowPercent", "%"],
      ["TRC Lying >3 Days (Cr)", "trcLyingOver3DaysCr", "Value"],
    ],
  },
  { title: "Issue", rows: [["Issue Description", "issueDescription", ""]] },
];

function today() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function asNumber(value) {
  if (value === "" || value === null || value === undefined) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function asInteger(value) {
  const n = asNumber(value);
  return n === null ? null : Math.round(n);
}

export default function DailyReport({ session, onSignOut }) {
  const navigate = useNavigate();
  const { customerId, categoryId } = useParams();
  const [customer, setCustomer] = useState(null);
  const [category, setCategory] = useState(null);
  const [lines, setLines] = useState([]);
  const [lineData, setLineData] = useState({});
  const [reportDate, setReportDate] = useState(today());
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState("");
  const [isImporting, setIsImporting] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const excelFileInput = useRef(null);
  const isSmtCategory = category?.categoryName?.trim().toUpperCase() === "SMT";
  const reportGroups = useMemo(() => GROUPS.map((group) => (
    group.title === "Production"
      ? {
          ...group,
          rows: group.rows.filter(([, key]) => (
            isSmtCategory
              ? key !== "upphInstalled" && key !== "upphActual"
              : key !== "cphTarget" && key !== "cphActual"
          )),
        }
      : group
  )), [isSmtCategory]);

  const loadReport = async (showLoader = true) => {
    if (showLoader) setLoading(true);
    setError("");
    try {
      const customers = await api.get("/catalog/customers");
      const foundCustomer = customers.find((x) => Number(x.customerId) === Number(customerId));
      const foundCategory = foundCustomer?.categories?.find((x) => Number(x.categoryId) === Number(categoryId));
      if (!foundCustomer || !foundCategory) throw new Error("Brand or category configuration was not found.");

      const statusRows = await api.get(`/daily-reports/line-status?customerId=${customerId}&categoryId=${categoryId}&reportDate=${reportDate}`);
      const catalogLines = Array.isArray(foundCategory.productionLines) ? foundCategory.productionLines : [];
      const mergedLines = catalogLines.map((line) => ({
        ...line,
        ...(statusRows.find((x) => Number(x.lineId) === Number(line.lineId)) || {}),
      }));

      const result = await Promise.all(mergedLines.map(async (line) => {
        const [config, entriesResponse] = await Promise.all([
          api.get(`/daily-reports/config?customerId=${customerId}&categoryId=${categoryId}&lineId=${line.lineId}&reportDate=${reportDate}`),
          api.get(`/daily-reports/entries?customerId=${customerId}&categoryId=${categoryId}&lineId=${line.lineId}&reportDate=${reportDate}`),
        ]);
        const saved = Array.isArray(entriesResponse) ? entriesResponse : [];
        const entries = saved
          .filter((item) => item.modelId !== null && item.modelId !== undefined)
          .map((item) => ({
            entryId: `saved-${item.dailyReportId}`,
            dailyReportId: item.dailyReportId,
            modelId: String(item.modelId),
            modelName: item.modelName || "Model",
            status: item.status || "DRAFT",
            isActive: item.isActive !== false,
            rejectionReason: item.rejectionReason || "",
            form: { ...EMPTY_FORM, ...item },
            isNew: false,
          }));

        return [line.lineId, {
          isActive: line.isActive !== false,
          status: line.status || "NOT_STARTED",
          models: Array.isArray(config.models) ? config.models : [],
          entries,
        }];
      }));

      setCustomer(foundCustomer);
      setCategory(foundCategory);
      setLines(mergedLines);
      setLineData(Object.fromEntries(result));
    } catch (e) {
      setError(e.message || "Unable to load daily report.");
    } finally {
      if (showLoader) setLoading(false);
    }
  };

  useEffect(() => {
    loadReport();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [customerId, categoryId, reportDate]);

  const updateEntry = (lineId, entryId, field, value) => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        status: "IN_PROGRESS",
        entries: current[lineId].entries.map((entry) =>
          entry.entryId === entryId
            ? { ...entry, status: entry.status === "APPROVED" ? entry.status : "DRAFT", form: { ...entry.form, [field]: value } }
            : entry
        ),
      },
    }));
    setNotice("");
  };

  const addModel = (lineId, modelId) => {
    if (!modelId) return;
    setLineData((current) => {
      const line = current[lineId];
      if (!line || line.entries.some((e) => String(e.modelId) === String(modelId))) return current;
      const model = line.models.find((m) => String(m.modelId) === String(modelId));
      if (!model) return current;
      return {
        ...current,
        [lineId]: {
          ...line,
          status: "IN_PROGRESS",
          entries: [...line.entries, {
            entryId: `new-${lineId}-${modelId}-${Date.now()}`,
            dailyReportId: null,
            modelId: String(modelId),
            modelName: model.modelName,
            status: "DRAFT",
            isActive: true,
            rejectionReason: "",
            form: { ...EMPTY_FORM },
            isNew: true,
          }],
        },
      };
    });
  };

  const removeModel = (lineId, entryId) => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        entries: current[lineId].entries.filter((entry) => entry.entryId !== entryId),
      },
    }));
  };

  const toggleLine = async (lineId) => {
    const line = lineData[lineId];
    if (!line) return;
    const next = !line.isActive;
    if (!next && !window.confirm("Mark this line inactive for this date? Its daily values will be cleared.")) return;
    setBusy(`toggle-${lineId}`); setError(""); setNotice("");
    try {
      await api.post("/daily-reports/line-status", {
        reportDate, customerId: Number(customerId), categoryId: Number(categoryId), lineId: Number(lineId), isActive: next,
      });
      await loadReport(false);
      setNotice(`${getLineName(lineId)} is now ${next ? "active" : "inactive"}.`);
    } catch (e) {
      setError(e.message || "Unable to update line status.");
    } finally { setBusy(""); }
  };

  const getLineName = (lineId) => lines.find((x) => Number(x.lineId) === Number(lineId))?.lineName || "Selected line";

  const buildPayload = (lineId, entry) => ({
    reportDate,
    customerId: Number(customerId), categoryId: Number(categoryId), lineId: Number(lineId), modelId: Number(entry.modelId),
    monthPlan: asNumber(entry.form.monthPlan), monthActual: asNumber(entry.form.monthActual),
    productionPlan: asNumber(entry.form.productionPlan), productionActual: asNumber(entry.form.productionActual),
    uphTarget: asNumber(entry.form.uphTarget), uphActual: asNumber(entry.form.uphActual),
    upphInstalled: isSmtCategory ? null : asNumber(entry.form.upphInstalled),
    upphActual: isSmtCategory ? null : asNumber(entry.form.upphActual),
    cphTarget: isSmtCategory ? asNumber(entry.form.cphTarget) : null,
    cphActual: isSmtCategory ? asNumber(entry.form.cphActual) : null,
    fpyTarget: asNumber(entry.form.fpyTarget), fpyActual: asNumber(entry.form.fpyActual),
    ftyTarget: asNumber(entry.form.ftyTarget), ftyActual: asNumber(entry.form.ftyActual),
    rtyTarget: asNumber(entry.form.rtyTarget), rtyActual: asNumber(entry.form.rtyActual),
    osdReportingDateValue: asNumber(entry.form.osdReportingDateValue), osdReportingDatePercent: asNumber(entry.form.osdReportingDatePercent),
    osdMtdValue: asNumber(entry.form.osdMtdValue), osdMtdPercent: asNumber(entry.form.osdMtdPercent),
    plannedOTManhours: asNumber(entry.form.plannedOTManhours), unplannedOTManhours: asNumber(entry.form.unplannedOTManhours),
    actualOTManhours: asNumber(entry.form.actualOTManhours), openWOQty: asInteger(entry.form.openWOQty),
    over7DaysWOBalanceQty: asInteger(entry.form.over7DaysWOBalanceQty), dailyTRCInQty: asInteger(entry.form.dailyTRCInQty),
    dailyTRCOutQty: asInteger(entry.form.dailyTRCOutQty), trcOverallFailureInflowPercent: asNumber(entry.form.trcOverallFailureInflowPercent),
    trcLyingOver3DaysCr: asNumber(entry.form.trcLyingOver3DaysCr), issueDescription: entry.form.issueDescription?.trim() || null,
  });

  const saveLine = async (lineId, submit) => {
    const line = lineData[lineId];
    if (!line?.isActive) return setError("Activate the line before saving or submitting it.");
    if (!line.entries.length) return setError(`Add at least one model to ${getLineName(lineId)}.`);
    setBusy(`${submit ? "submit" : "save"}-${lineId}`); setError(""); setNotice("");
    try {
      for (const entry of line.entries) {
        if (!entry.modelId) throw new Error(`Select a model for every column in ${getLineName(lineId)}.`);
        await api.post(`/daily-reports/${submit ? "submit" : "draft"}`, buildPayload(lineId, entry));
      }
      await loadReport(false);
      setNotice(`${getLineName(lineId)} ${submit ? "submitted for approval" : "draft saved"}.`);
    } catch (e) {
      setError(e.message || "Unable to save the report.");
    } finally { setBusy(""); }
  };

  const importExcel = async (event) => {
    const file = event.target.files?.[0];
    if (!file) return;

    setIsImporting(true);
    setError("");
    setNotice("");

    try {
      const formData = new FormData();
      formData.append("file", file);
      formData.append("customerId", String(customerId));
      formData.append("categoryId", String(categoryId));
      formData.append("reportDate", reportDate);

      const preview = await api.postFormData(
        "/daily-reports/import/preview",
        formData
      );
      const pageLines = new Map(lines.map((line) => [Number(line.lineId), line]));
      const validationErrors = [];

      for (const imported of preview.data || []) {
        const line = pageLines.get(Number(imported.lineId));
        const lineState = lineData[imported.lineId];
        if (!line || !lineState) {
          validationErrors.push(`Line ${imported.lineNo} is not available on this report page.`);
          continue;
        }

        const modelAvailable = lineState.models.some(
          (model) => Number(model.modelId) === Number(imported.modelId)
        );
        if (!modelAvailable) {
          validationErrors.push(
            `Model ${imported.modelName} is not available for line ${imported.lineNo} on this page.`
          );
        }

        const existingEntry = lineState.entries.find(
          (entry) => Number(entry.modelId) === Number(imported.modelId)
        );
        if (existingEntry?.status === "APPROVED" || existingEntry?.status === "SUBMITTED") {
          validationErrors.push(
            `Model ${imported.modelName} on line ${imported.lineNo} has already been submitted and cannot be replaced.`
          );
        }
      }

      if (validationErrors.length) {
        throw new Error(validationErrors.join(" "));
      }

      setLineData((current) => {
        const next = { ...current };
        const grouped = new Map();

        for (const imported of preview.data || []) {
          const lineId = Number(imported.lineId);
          const entries = grouped.get(lineId) || [];
          entries.push(imported);
          grouped.set(lineId, entries);
        }

        for (const [lineId, importedEntries] of grouped) {
          const currentLine = next[lineId];
          const entries = [...currentLine.entries];

          for (const imported of importedEntries) {
            const existingIndex = entries.findIndex(
              (entry) => Number(entry.modelId) === Number(imported.modelId)
            );
            if (existingIndex >= 0) {
              const existing = entries[existingIndex];
              entries[existingIndex] = {
                ...existing,
                form: { ...EMPTY_FORM, ...imported.form },
                status: "DRAFT",
              };
            } else {
              entries.push({
                entryId: `import-${lineId}-${imported.modelId}-${Date.now()}-${entries.length}`,
                dailyReportId: null,
                modelId: String(imported.modelId),
                modelName: imported.modelName,
                status: "DRAFT",
                isActive: true,
                rejectionReason: "",
                form: { ...EMPTY_FORM, ...imported.form },
                isNew: true,
              });
            }
          }

          next[lineId] = { ...currentLine, status: "IN_PROGRESS", entries };
        }

        return next;
      });

      setNotice(
        `Excel imported successfully — ${preview.lines} lines, ${preview.models} models. Please review the data before submitting.`
      );
    } catch (e) {
      setError(
        e.message?.startsWith("Excel import failed")
          ? e.message
          : `Excel import failed: ${e.message || "Unable to read the workbook."}`
      );
    } finally {
      setIsImporting(false);
      event.target.value = "";
    }
  };

  const columns = useMemo(() => {
    const result = [];
    lines.forEach((line) => (lineData[line.lineId]?.entries || []).forEach((entry) => result.push({ lineId: line.lineId, entry })));
    return result;
  }, [lines, lineData]);

  const completed = lines.filter((line) => ["SUBMITTED", "APPROVED", "INACTIVE"].includes(lineData[line.lineId]?.status)).length;

  return (
    <ContributorLayout user={session?.user} onSignOut={onSignOut}>
      <main className="daily-report-page">
        <div className="daily-report-content">
          <ProcessTracker activeStep="report" customerId={customerId} categoryId={categoryId} />

          <button className="report-back" onClick={() => navigate(`/contributor/brands/${customerId}/categories`)}>
            <ArrowLeft size={16} /> Back to categories
          </button>

          <div className="report-breadcrumb">{customer?.customerName || "Brand"}<span>›</span>{category?.categoryName || "FATP"}<span>›</span><strong>Daily Report</strong></div>

          <header className="report-title-row">
            <div className="report-title-icon"><ClipboardList size={25} /></div>
            <div><h1>{category?.categoryName || "Daily"} Daily Report</h1><p>Enter all Plan, Target, Installed and Actual values manually for each model.</p></div>
            <button
              className="secondary-action report-excel-upload"
              type="button"
              style={{ marginLeft: "auto", flexShrink: 0 }}
              disabled={isImporting || Boolean(busy)}
              onClick={() => excelFileInput.current?.click()}
            >
              {isImporting ? <Loader2 size={14} className="spin" /> : <Upload size={14} />}
              {isImporting ? "Reading Excel..." : "Upload Excel"}
            </button>
            <input
              ref={excelFileInput}
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              hidden
              onChange={importExcel}
            />
          </header>

          <div className="report-context-bar">
            <div><span>BRAND</span><strong>{customer?.customerName || "—"}</strong></div>
            <div><span>CATEGORY</span><strong>{category?.categoryName || "—"}</strong></div>
            <label><span>REPORT DATE</span><input type="date" max={today()} value={reportDate} onChange={(e) => setReportDate(e.target.value)} /></label>
            <div><span>PROGRESS</span><strong>{completed}/{lines.length} lines completed</strong></div>
          </div>

          {(error || notice) && (
            <div className={`report-message ${error ? "error" : "success"}`}>
              {error ? <AlertCircle size={18} /> : <CheckCircle2 size={18} />}
              <div><strong>{error ? "Please check the report" : "Saved"}</strong><p>{error || notice}</p></div>
              <button onClick={() => { setError(""); setNotice(""); }}><X size={16} /></button>
            </div>
          )}

          {loading ? (
            <div className="report-message info"><Loader2 size={18} className="spin" /><div><strong>Loading FATP report</strong><p>Loading lines, models and saved daily values…</p></div></div>
          ) : (
            <section className="matrix-shell">
              <div className="matrix-scroll">
                <table className="daily-matrix">
                  <thead>
                    <tr className="line-header-row">
                      <th className="parameter-head sticky-col" rowSpan="2">PARAMETERS</th>
                      {lines.map((line) => {
                        const count = Math.max(lineData[line.lineId]?.entries?.length || 0, 1);
                        return (
                          <th key={line.lineId} className="line-group-head" colSpan={count}>
                            <div className="line-head-inner">
                              <div><strong>{line.lineName}</strong><span>{line.sapLocation || "SAP location not set"}</span></div>
                              <button className={`matrix-line-toggle ${lineData[line.lineId]?.isActive === false ? "off" : ""}`} onClick={() => toggleLine(line.lineId)} disabled={busy === `toggle-${line.lineId}`}>
                                {lineData[line.lineId]?.isActive === false ? <ToggleLeft size={18} /> : <ToggleRight size={18} />}
                              </button>
                            </div>
                          </th>
                        );
                      })}
                    </tr>
                    <tr className="model-header-row">
                      {lines.map((line) => {
                        const data = lineData[line.lineId] || { entries: [], models: [] };
                        if (!data.entries.length) {
                          return <th key={line.lineId} className="model-head empty-model-head"><AddModelSelect models={data.models} onSelect={(id) => addModel(line.lineId, id)} disabled={!data.isActive} /></th>;
                        }
                        return data.entries.map((entry) => (
                          <th key={entry.entryId} className="model-head">
                            <div className="model-head-content"><div><span>MODEL</span><strong>{entry.modelName}</strong></div>{entry.isNew && <button onClick={() => removeModel(line.lineId, entry.entryId)} title="Remove model"><Trash2 size={13} /></button>}</div>
                          </th>
                        ));
                      })}
                    </tr>
                  </thead>
                  <tbody>
                    {reportGroups.map((group) => <MatrixGroup key={group.title} group={group} columns={columns} lineData={lineData} onChange={updateEntry} />)}
                  </tbody>
                </table>
              </div>

              <div className="matrix-add-model-bar">
                {lines.map((line) => {
                  const data = lineData[line.lineId] || { entries: [], models: [] };
                  const available = (data.models || []).filter((m) => !data.entries.some((e) => String(e.modelId) === String(m.modelId)));
                  if (!available.length) return null;
                  return <div className="line-add-model" key={line.lineId}><strong>{line.lineName}</strong><AddModelSelect models={available} onSelect={(id) => addModel(line.lineId, id)} disabled={!data.isActive} /></div>;
                })}
              </div>

              <div className="matrix-actions">
                <span>Every model column is an independent daily record. No Plan or Target is fetched from KPI_Master.</span>
                <div className="action-groups">
                  {lines.map((line) => {
                    const data = lineData[line.lineId] || { entries: [] };
                    if (!data.entries.length) return null;
                    return <div className="line-action-group" key={line.lineId}><strong>{line.lineName}</strong>
                      <button className="secondary-action" disabled={busy === `save-${line.lineId}` || data.isActive === false} onClick={() => saveLine(line.lineId, false)}>{busy === `save-${line.lineId}` ? <Loader2 size={14} className="spin" /> : "Save Draft"}</button>
                      <button className="primary-action" disabled={busy === `submit-${line.lineId}` || data.isActive === false} onClick={() => saveLine(line.lineId, true)}>{busy === `submit-${line.lineId}` ? <Loader2 size={14} className="spin" /> : <><Send size={14} /> Submit</>}</button>
                    </div>;
                  })}
                </div>
              </div>
            </section>
          )}
        </div>
      </main>
    </ContributorLayout>
  );
}

function AddModelSelect({ models, onSelect, disabled }) {
  return <label className="add-model-control"><Plus size={13} /><select value="" onChange={(e) => onSelect(e.target.value)} disabled={disabled}><option value="">Add model</option>{models.map((model) => <option key={model.modelId} value={model.modelId}>{model.modelName}</option>)}</select></label>;
}

function MatrixGroup({ group, columns, lineData, onChange }) {
  return <>
    <tr className="group-row"><th className="sticky-col group-label">{group.title}</th>{columns.map(({ entry }) => <td className="group-spacer" key={`${group.title}-${entry.entryId}`} />)}</tr>
    {group.rows.map(([label, key, sub]) => <tr key={key} className={key === "issueDescription" ? "issue-row" : ""}>
      <th className="sticky-col parameter-label"><span>{label}</span>{sub && <small>{sub}</small>}</th>
      {columns.map(({ lineId, entry }) => {
        const disabled = entry.status === "APPROVED" || lineData[lineId]?.isActive === false;
        return <td className="matrix-cell" key={`${key}-${entry.entryId}`}>
          {key === "issueDescription" ? <textarea value={entry.form?.[key] ?? ""} disabled={disabled} placeholder="Issue / observation" onChange={(e) => onChange(lineId, entry.entryId, key, e.target.value)} />
            : <input type="number" min="0" step="0.01" value={entry.form?.[key] ?? ""} disabled={disabled} placeholder="—" onChange={(e) => onChange(lineId, entry.entryId, key, e.target.value)} />}
        </td>;
      })}
    </tr>)}
  </>;
}
