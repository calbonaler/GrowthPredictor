window.addEventListener("DOMContentLoaded", function() {
	let gChart = null;

	function plotData(data, xTitle, yTitle, displayPoint) {
		const dataset = {
			data: data,
			borderColor: "red",
			backgroundColor: "red",
			showLine: true,
		};
		if (!displayPoint) {
			dataset.pointRadius = 0;
			dataset.pointHitRadius = 0;
		}
		if (gChart != null) {
			gChart.destroy();
		}
		let canvas = document.getElementById("chart-area");
		if (canvas == null) {
			const container = document.getElementById("chart-container");
			canvas = document.createElement("canvas");
			canvas.id = "chart-area";
			container.innerHTML = "";
			container.appendChild(canvas);
		}
		gChart = new Chart(canvas, {
			type: "scatter",
			data: {
				datasets: [dataset],
			},
			options: {
				plugins: { legend: { display: false } },
				scales: {
					x: {
						title: { display: true, text: xTitle },
						ticks: {
							min: 80,
							stepSize: 1,
						},
					},
					y: { min: 0, title: { display: true, text: yTitle } }
				},
				responsive: true,
				maintainAspectRatio: false,
			},
		});
	}
	function displayText(text) {
		if (gChart != null) {
			gChart.destroy();
			gChart = null;
		}
		const container = document.getElementById("chart-container");
		container.innerHTML = "";
		container.textContent = text;
	}
	function plotGrowthDistribution(name, xTitle, age) {
		const dist = gGrowthData[age][name];
		const xRange = dist.sd * 6;
		const data = [];
		const halfDataPoints = 100;
		const doubleVariance = 2 * dist.sd * dist.sd;
		const coeff = 1 / Math.sqrt(Math.PI * doubleVariance);
		for (let i = -halfDataPoints; i <= halfDataPoints; i++) {
			const offsetX = xRange * i / halfDataPoints;
			const y = coeff * Math.exp(-offsetX * offsetX / doubleVariance);
			data.push({x: dist.mean + offsetX, y: y});
		}
		plotData(data, xTitle, "Likelihood", false);
	}
	function plotHeightDistribution({age}) { plotGrowthDistribution("height", "Height (cm)", age); }
	function plotWeightDistribution({age}) { plotGrowthDistribution("weight", "Weight (kg)", age); }
	function plotAgeGroupProbabilities({height, heightRange, weight, weightRange}) {
		if (height == null && weight == null) {
			alert("Either height or weight is required to compute age.");
			return;
		}
		const {name, value, range} = height != null ? {name: "height", value: height, range: heightRange} :
		                                              {name: "weight", value: weight, range: weightRange};
		const data = [];
		for (const age in gGrowthData) {
			const dist = gGrowthData[age][name];
			const maxV = math.erf((value + range - dist.mean) / dist.sd / Math.sqrt(2));
			const minV = math.erf((value - dist.mean) / dist.sd / Math.sqrt(2));
			const prob = (maxV - minV) / 2;
			data.push({x: Number(age), y: prob});
		}
		plotData(data, "Age", "Probability", true);
	}
	function computeGis({age, height, weight}) {
		if (height == null && weight == null) {
			alert("Either height or weight is required to compute age.");
			return;
		}
		const {name, value} = height != null ? {name: "height", value: height} :
		                                       {name: "weight", value: weight};
		const dist = gGrowthData[age][name];
		const prob = (1 + math.erf((value - dist.mean) / dist.sd / Math.sqrt(2))) / 2;
		displayText(prob.toExponential(20));
	}

	const gActions = {
		"height": {callback: plotHeightDistribution,    usingInputs: ["age"],                                            requiredInputs: ["age"]},
		"weight": {callback: plotWeightDistribution,    usingInputs: ["age"],                                            requiredInputs: ["age"]},
		"age"   : {callback: plotAgeGroupProbabilities, usingInputs: ["height", "heightRange", "weight", "weightRange"], requiredInputs: []     },
		"gis"   : {callback: computeGis,                usingInputs: ["age", "height", "weight"],                        requiredInputs: ["age"]},
	};

	// assign IDs for each input element
	for (const input of document.getElementById("inputs").children) {
		input.children[0].htmlFor = input.id + "-input";
		input.children[1].id = input.id + "-input";
	}
	// change inputs' visibility when Graph/Scale select has changed
	const graphScaleInput = document.getElementById("inputs-graphScale-input");
	function onGraphScaleInputChanged() {
		const usingInputs = gActions[this.value].usingInputs;
		for (const input of document.getElementById("inputs").children) {
			if (input !== this.parentElement && usingInputs.indexOf(input.id.slice("inputs-".length)) < 0) {
				input.style.visibility = "collapse";
			} else {
				input.style.visibility = "";
			}
		}
	}
	graphScaleInput.addEventListener("change", onGraphScaleInputChanged);
	onGraphScaleInputChanged.call(graphScaleInput);
	// refresh
	function parseNumber(str) {
		if (str === "") {
			return null;
		}
		let value = Number(str);
		if (Number.isNaN(value)) {
			return null;
		}
		return value;
	}
	function parseValueAndRange(key, args) {
		const str = document.getElementById("inputs-" + key + "-input").value;
		args[key] = parseNumber(str);
		let range = parseNumber(document.getElementById("inputs-" + key + "Range-input").value);
		if (range == null) {
			range = 1;
			const decimalPointIndex = str.indexOf(".");
			if (decimalPointIndex >= 0) {
				const fractionDigit = str.length - decimalPointIndex - 1;
				range = Math.pow(10, -fractionDigit);
			}
		}
		args[key + "Range"] = range;
	}
	const refreshButton = document.getElementById("refresh-button");
	refreshButton.addEventListener("click", function() {
		const {callback, requiredInputs} = gActions[graphScaleInput.value];
		const args = {};
		for (const input of document.getElementById("inputs").children) {
			const key = input.id.slice("inputs-".length);
			args[key] = parseNumber(input.children[1].value);
		}
		// age
		args.age = parseNumber(document.getElementById("inputs-age-input").value);
		// height, heightRange
		parseValueAndRange("height", args);
		// weight, weightRange
		parseValueAndRange("weight", args);
		for (const key of requiredInputs) {
			if (args[key] == null) {
				alert(`Required parameter "${key}" is not specified.`);
				return;
			}
		}
		callback(args);
	});
});
