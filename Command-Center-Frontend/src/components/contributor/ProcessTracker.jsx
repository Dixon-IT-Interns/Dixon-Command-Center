import { Check } from "lucide-react";
import { useNavigate } from "react-router-dom";
import "./ProcessTracker.css";

const STEPS = [
  { key: "brand", label: "Brand" },
  { key: "category", label: "Category" },
  { key: "report", label: "Report" },
];

export default function ProcessTracker({ activeStep, customerId, categoryId }) {
  const navigate = useNavigate();
  const activeIndex = Math.max(0, STEPS.findIndex((step) => step.key === activeStep));

  const goTo = (index) => {
    if (index === 0) navigate("/contributor/brands");
    if (index === 1 && customerId) navigate(`/contributor/brands/${customerId}/categories`);
    if (index === 2 && customerId && categoryId) navigate(`/contributor/brands/${customerId}/categories/${categoryId}/report`);
  };

  return (
    <nav className="dcc-process-tracker" aria-label="Reporting progress">
      {STEPS.map((step, index) => {
        const isActive = index === activeIndex;
        const isDone = index < activeIndex;
        const clickable = index <= activeIndex;
        return (
          <div className="dcc-process-item" key={step.key}>
            <button
              type="button"
              className={`dcc-process-step ${isActive ? "active" : ""} ${isDone ? "done" : ""}`}
              onClick={() => clickable && goTo(index)}
              disabled={!clickable}
              aria-current={isActive ? "step" : undefined}
            >
              <span className="dcc-process-number">{isDone ? <Check size={13} strokeWidth={3} /> : index + 1}</span>
              <span>{step.label}</span>
            </button>
            {index < STEPS.length - 1 && <i className={index < activeIndex ? "done" : ""} />}
          </div>
        );
      })}
    </nav>
  );
}
