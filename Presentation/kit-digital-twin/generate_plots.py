from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd


ROOT = Path(__file__).resolve().parent
PLOTS = ROOT / "plots"
PLOTS.mkdir(exist_ok=True)
DATA = pd.read_csv(ROOT / "kit-comparison.csv")

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "font.size": 11,
    "axes.titleweight": "bold",
    "axes.labelcolor": "#334155",
    "xtick.color": "#334155",
    "ytick.color": "#334155",
})

kit_colors = ["#2563EB", "#7C3AED", "#DB2777", "#EA580C", "#16A34A"]


def clean_axes(axis):
    axis.spines["top"].set_visible(False)
    axis.spines["right"].set_visible(False)
    axis.grid(axis="y", color="#E2E8F0", linewidth=0.8)
    axis.set_axisbelow(True)


fig, axes = plt.subplots(1, 2, figsize=(13.33, 6.2), facecolor="white")
bars = axes[0].bar(DATA["kit"], DATA["cad_components"], color=kit_colors)
axes[0].set_title("Segmented CAD components")
axes[0].set_ylabel("Component groups")
axes[0].bar_label(bars, padding=4, fontweight="bold")
clean_axes(axes[0])

triangles_m = DATA["triangles"] / 1_000_000
bars = axes[1].bar(DATA["kit"], triangles_m, color=kit_colors)
axes[1].set_title("Rendered mesh complexity")
axes[1].set_ylabel("Triangles (millions)")
axes[1].bar_label(bars, labels=[f"{value:.2f}M" for value in triangles_m], padding=4, fontweight="bold")
clean_axes(axes[1])
fig.suptitle("CAD complexity across the five modular automation kits", fontsize=17, fontweight="bold")
fig.text(0.5, 0.01, "Prepared assembly manifests; Kit 4–5 values describe CAD assemblies, not completed simulations.", ha="center", color="#64748B")
fig.tight_layout(rect=(0, 0.05, 1, 0.92))
fig.savefig(PLOTS / "cad-complexity.png", dpi=180, bbox_inches="tight")
plt.close(fig)


fig, axis = plt.subplots(figsize=(13.33, 6.2), facecolor="white")
x = np.arange(len(DATA))
width = 0.24
axis.bar(x - width, DATA["bounds_x_mm"], width, label="X", color="#2563EB")
axis.bar(x, DATA["bounds_y_mm"], width, label="Y", color="#7C3AED")
axis.bar(x + width, DATA["bounds_z_mm"], width, label="Z", color="#16A34A")
axis.set_xticks(x, DATA["kit"])
axis.set_ylabel("Prepared assembly extent (mm)")
axis.set_title("Assembly envelope comparison")
axis.legend(frameon=False, ncols=3, loc="upper left")
clean_axes(axis)
fig.text(0.5, 0.01, "Extents are calculated from the segmented presentation meshes.", ha="center", color="#64748B")
fig.tight_layout(rect=(0, 0.05, 1, 1))
fig.savefig(PLOTS / "assembly-envelope.png", dpi=180, bbox_inches="tight")
plt.close(fig)


features = [
    "Rigid-body workpieces",
    "Automatic sequence",
    "PLC-neutral I/O",
    "Engineering dashboard",
    "Fault / safe-stop logic",
    "Stamping",
    "Vacuum handling",
    "Rotary routing",
    "Historian + CSV",
]
coverage = np.array([
    [1, 1, 1, 1, 1, 0, 0, 0, 1],
    [1, 1, 1, 1, 1, 1, 0, 0, 0],
    [1, 1, 1, 1, 1, 1, 1, 1, 0],
])
fig, axis = plt.subplots(figsize=(13.33, 5.6), facecolor="white")
axis.imshow(coverage, cmap=plt.matplotlib.colors.ListedColormap(["#E2E8F0", "#22C55E"]), aspect="auto", vmin=0, vmax=1)
axis.set_xticks(np.arange(len(features)), features, rotation=30, ha="right")
axis.set_yticks(np.arange(3), ["Kit 1", "Kit 2", "Kit 3"])
axis.set_title("Validated offline simulation capability matrix")
for row in range(coverage.shape[0]):
    for column in range(coverage.shape[1]):
        axis.text(column, row, "YES" if coverage[row, column] else "—", ha="center", va="center", fontweight="bold", color="#0F172A")
axis.set_xticks(np.arange(-0.5, len(features), 1), minor=True)
axis.set_yticks(np.arange(-0.5, 3, 1), minor=True)
axis.grid(which="minor", color="white", linewidth=2)
axis.tick_params(which="minor", bottom=False, left=False)
fig.text(0.5, 0.01, "Kit 4 and Kit 5 are intentionally excluded because only their assemblies are presented.", ha="center", color="#64748B")
fig.tight_layout(rect=(0, 0.06, 1, 1))
fig.savefig(PLOTS / "simulation-capability-matrix.png", dpi=180, bbox_inches="tight")
plt.close(fig)

print("Generated presentation plots in", PLOTS)
